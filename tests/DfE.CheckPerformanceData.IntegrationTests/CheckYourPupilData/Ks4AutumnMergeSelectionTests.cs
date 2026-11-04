using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// #747 (AB#312017): the KS4 Autumn merge journey's selection rules, through the repository path
// the suggestions endpoint uses. Merge_KS4Autumn.json says what each page asks for: the first
// record from the included pupils, the second from every pupil of the school except the first.
// Both pages find a pupil by first name, last name, CYPMD ID, UPN or date of birth (#716). These
// prove a KS4 Autumn window gives exactly that, and that its suggestion rows carry the ID and the
// inclusion status the pages rely on.
[Collection(nameof(PostgresCollection))]
public sealed class Ks4AutumnMergeSelectionTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";

    /// <summary>The already-chosen first record, which the second page has to exclude.</summary>
    private static readonly Guid FirstRecordId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

    private static CheckingWindow NewKs4AutumnWindow(Guid id) => new()
    {
        Id = id,
        Title = "KS4 Autumn test window",
        StartDate = Unspecified(DateTime.UtcNow.AddDays(-7)),
        EndDate = Unspecified(DateTime.UtcNow.AddDays(7)),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4Autumn,
    };

    // Generated records in the shape of the dev seed's Kingsmead data: the Casey Carter pair
    // (800001 included, 800002 not) and one pupil from each inclusion group. The matcher tests
    // the start of a word, so a digit prefix meets a CYPMD ID and not the letter-led UPN.
    private static PupilRecord NewPupil(string firstname, string surname, string cypmdId, bool included, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Urn = long.Parse(TestUrn),
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = firstname,
        Sex = "F",
        DateOfBirth = "15/03/2010",
        Age = 15,
        FirstLanguage = "ENG",
        Pincl = included ? 401 : 402,
        Included = included,
        NewMobile = false,
        ActualYearGroup = "11",
        Ethnicity = "WOTH",
        SenF = "N",
        EntryDate = "01/09/2021",
        Cypmd_Id = cypmdId,
        MatchRef = 1,
        Upn = $"A{cypmdId.PadLeft(12, '0')}",
    };

    private static PupilRecord[] KingsmeadLikeSchool() =>
    [
        NewPupil("Casey", "Carter", "800001", included: true, id: FirstRecordId),
        NewPupil("Casey", "Carter", "800002", included: false),
        NewPupil("Alice", "Smith", "000001", included: true),
        NewPupil("Alice", "Johnson", "000201", included: false),
    ];

    private async Task<(Guid WindowId, CheckYourPupilDataRepository Repo)> SeededAsync()
    {
        await ResetAsync();
        var windowId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingWindows.Add(NewKs4AutumnWindow(windowId));
            await ctx.SaveChangesAsync();
        }

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, KingsmeadLikeSchool());

        return (windowId, new CheckYourPupilDataRepository(
            fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions())));
    }

    // ── First record: included pupils only ───────────────────────────────────

    [Fact]
    public async Task The_first_record_search_offers_the_included_copy_only()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Carter", PupilFilter.Included);

        var row = Assert.Single(results);
        Assert.Equal(FirstRecordId, row.Id);
        Assert.Contains("CYPMD ID: 800001,", row.Label);
        Assert.Contains("Status: INCLUDED", row.Label);
    }

    [Fact]
    public async Task A_non_included_pupil_is_not_offered_as_the_first_record()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Johnson", PupilFilter.Included);

        Assert.Empty(results);
    }

    // ── Second record: any pupil of the school except the first ──────────────

    [Fact]
    public async Task The_second_record_search_by_the_shared_name_offers_the_other_copy_and_not_the_first_record()
    {
        var (windowId, repo) = await SeededAsync();

        // #716: the page finds a pupil by name. Both copies share the name, so only the exclusion
        // of the first record keeps it to one row.
        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Carter", PupilFilter.All, excludeId: FirstRecordId);

        var row = Assert.Single(results);
        Assert.NotEqual(FirstRecordId, row.Id);
        Assert.Contains("CYPMD ID: 800002,", row.Label);
        Assert.Contains("Status: NOT INCLUDED", row.Label);
    }

    [Fact]
    public async Task The_second_record_search_by_a_cypmd_id_prefix_offers_the_other_copy_and_not_the_first_record()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "8000", PupilFilter.All, excludeId: FirstRecordId);

        var row = Assert.Single(results);
        Assert.Contains("CYPMD ID: 800002,", row.Label);
        Assert.Contains("Status: NOT INCLUDED", row.Label);
    }

    [Theory]
    [InlineData("0000", "000001", "Status: INCLUDED")]
    [InlineData("Johnson", "000201", "Status: NOT INCLUDED")]
    public async Task The_second_record_search_offers_included_and_non_included_pupils(
        string query, string expectedCypmdId, string expectedStatus)
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, query, PupilFilter.All, excludeId: FirstRecordId);

        var row = Assert.Single(results);
        Assert.Contains($"CYPMD ID: {expectedCypmdId},", row.Label);
        Assert.Contains(expectedStatus, row.Label);
    }
}
