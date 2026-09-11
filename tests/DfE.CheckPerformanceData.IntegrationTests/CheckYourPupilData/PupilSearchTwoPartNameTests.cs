using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// US3: The Include journey's pupil-search step inherits two-part name matching via
// PupilSuggestionFormat.Matches. These integration tests verify SearchPupilsAsync returns
// correct results for two-part queries with both Included and NonIncluded filters.
[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchTwoPartNameTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";

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
        Title = "Test Window",
        StartDate = Unspecified(DateTime.UtcNow.AddDays(-7)),
        EndDate = Unspecified(DateTime.UtcNow.AddDays(7)),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
    };

    private static PupilRecord NewPupil(
        string firstname, string surname, string cypmdId, bool included = true) => new()
    {
        Id = Guid.NewGuid(),
        Urn = long.Parse(TestUrn),
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = firstname,
        Sex = "M",
        DateOfBirth = "01/01/2000",
        Age = 16,
        FirstLanguage = "English",
        Pincl = included ? 401 : 402,
        NewMobile = false,
        ActualYearGroup = "11",
        Ethnicity = "A1",
        SenF = "N",
        EntryDate = "01/09/2021",
        Cypmd_Id = cypmdId,
        MatchRef = 1,
        Upn = $"A{cypmdId.PadLeft(12, '0')}",
    };

    private async Task<(Guid WindowId, CheckYourPupilDataRepository Repo)> SeededAsync(params PupilRecord[] pupils)
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingWindows.Add(NewWindow(windowId));
            await ctx.SaveChangesAsync();
        }

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, pupils);

        return (windowId, new CheckYourPupilDataRepository(
            fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions())));
    }

    [Fact]
    public async Task Two_part_query_matches_both_parts_in_included_pupils()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", included: true),
            NewPupil("Jane", "Smith", "500002", included: true),
            NewPupil("John", "Jones", "500003", included: true));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "John Smith", PupilFilter.Included);

        Assert.Single(results);
        Assert.Contains("John", results[0].Label);
        Assert.Contains("Smith", results[0].Label);
    }

    [Fact]
    public async Task Two_part_query_matches_containing_names_in_included_pupils()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("Johnny", "Smithson", "500001", included: true),
            NewPupil("Jane", "Smith", "500002", included: true));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "John Smith", PupilFilter.Included);

        // "Johnny" contains "John" and "Smithson" contains "Smith"
        Assert.Single(results);
        Assert.Contains("Johnny", results[0].Label);
    }

    [Fact]
    public async Task Two_part_query_matches_both_parts_in_non_included_pupils()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", included: false),
            NewPupil("Jane", "Smith", "500002", included: false));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "John Smith", PupilFilter.NonIncluded);

        Assert.Single(results);
        Assert.Contains("John", results[0].Label);
    }

    [Fact]
    public async Task Two_part_query_excludes_when_only_one_part_matches()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Jones", "500001", included: true),
            NewPupil("Jane", "Smith", "500002", included: true));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "John Smith", PupilFilter.Included);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Single_term_query_still_works_after_split_implementation()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", included: true),
            NewPupil("Jane", "Smith", "500002", included: true));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.Included);

        Assert.Equal(2, results.Count);
    }
}
