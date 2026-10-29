using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// #711: the Include journey on 16-19 may pick a student from the non-included file or a student
// the included file carries with P_INCL 502. Ingress stamps INCLUDABLE for both; the repository
// only reads the stamp. Included stays the file of origin, so the 502 student is still included
// for Remove and Merge.
[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchIncludableFilterTests(PostgresFixture fixture)
{
    private const string TestUrn = "136309";
    private const string TestLaestab = "860/4070";

    private async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE ""ChangeRequests"" CASCADE;
            TRUNCATE ""CheckingWindows"" RESTART IDENTITY CASCADE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private static DateTime Unspecified(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    private static CheckingWindow NewWindow(Guid id) => new()
    {
        Id = id,
        Title = "16 to 19 test window",
        StartDate = Unspecified(DateTime.UtcNow.AddDays(-7)),
        EndDate = Unspecified(DateTime.UtcNow.AddDays(7)),
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
    };

    private static Post16PupilRecord NewStudent(string firstname, string surname, string cypmdId,
        bool included, bool includable, int? pincl) => new()
    {
        Id = Guid.NewGuid(),
        Included = included,
        Includable = includable,
        Cypmd_Id = cypmdId,
        Surname = surname,
        Firstname = firstname,
        Sex = "F",
        DateOfBirth = "01/09/2007",
        Age = 18,
        Pincl = pincl,
        PinclAims = included ? 503 : null,
        Laestab = "8604070",
        Urn = TestUrn,
        Ukprn = "10001234",
        Uln = $"99{cypmdId.PadLeft(8, '0')}",
    };

    private async Task<(Guid WindowId, CheckYourPupilDataRepository Repo)> SeededAsync(params Post16PupilRecord[] students)
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingWindows.Add(NewWindow(windowId));
            await ctx.SaveChangesAsync();
        }

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, students);

        return (windowId, new CheckYourPupilDataRepository(
            fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions())));
    }

    private static (Post16PupilRecord Alice501, Post16PupilRecord Bob502, Post16PupilRecord AliceNonIncluded) Students() =>
    (
        NewStudent("Alice", "Smith", "500001", included: true, includable: false, pincl: 501),
        NewStudent("Bob", "Smith", "500002", included: true, includable: true, pincl: 502),
        NewStudent("Alice", "Johnson", "500201", included: false, includable: true, pincl: null)
    );

    [Fact]
    public async Task Includable_returns_the_502_student_and_the_non_included_student()
    {
        var (alice501, bob502, aliceNonIncluded) = Students();
        var (windowId, repo) = await SeededAsync(alice501, bob502, aliceNonIncluded);

        var smiths = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Includable);
        var johnsons = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Johnson", PupilFilter.Includable);
        var results = smiths.Concat(johnsons).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Id == bob502.Id);
        Assert.Contains(results, r => r.Id == aliceNonIncluded.Id);
    }

    [Fact]
    public async Task NotIncludable_returns_only_the_student_who_is_included_for_good()
    {
        var (alice501, bob502, aliceNonIncluded) = Students();
        var (windowId, repo) = await SeededAsync(alice501, bob502, aliceNonIncluded);

        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Alice", PupilFilter.NotIncludable);

        var only = Assert.Single(results);
        Assert.Equal(alice501.Id, only.Id);
    }

    [Fact]
    public async Task Included_still_counts_the_502_student_as_included()
    {
        var (alice501, bob502, aliceNonIncluded) = Students();
        var (windowId, repo) = await SeededAsync(alice501, bob502, aliceNonIncluded);

        var results = await repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Id == aliceNonIncluded.Id);
    }

    // A filter the repository has no rule for fails loudly, so a new PupilFilter value cannot
    // quietly return the wrong pupils.
    [Fact]
    public async Task An_unknown_filter_throws()
    {
        var (alice501, bob502, aliceNonIncluded) = Students();
        var (windowId, repo) = await SeededAsync(alice501, bob502, aliceNonIncluded);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "Smith", (PupilFilter)99));
    }
}
