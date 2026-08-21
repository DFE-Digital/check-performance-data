using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// A results enquiry may only name a student the school holds a result for — there is no grade to
// correct otherwise. The Application service resolves that set of CYPMD ids from the results file
// and hands it down; the repository only applies it, so Persistence never learns what a result is.
//
// The window type here is KS4June: the restriction is window-type agnostic, and the KS4 record is
// the simpler fixture. What the 16-19 flow asks for is covered by the flow-config tests.
[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchResultsFilterTests(PostgresFixture fixture)
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

    // PostgreSQL 'timestamp without time zone' columns require DateTimeKind.Unspecified.
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

    // CheckingWindowId is left unset: pupils are read from the blob, which is already keyed by
    // window, so nothing in the search path reads it.
    private static PupilRecord NewPupil(string surname, string cypmdId) => new()
    {
        Id = Guid.NewGuid(),
        Urn = long.Parse(TestUrn),
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = "Test",
        Sex = "M",
        DateOfBirth = "01/01/2000",
        Age = 16,
        FirstLanguage = "English",
        Pincl = 401,
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
    public async Task Only_students_in_the_allow_list_are_suggested()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("Smith", "500001"),
            NewPupil("Smithson", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All,
            cypmdIdAllowList: new HashSet<string> { "500001" });

        var only = Assert.Single(results);
        Assert.StartsWith("Smith,", only.Label);
    }

    [Fact]
    public async Task A_null_allow_list_leaves_the_search_unrestricted()
    {
        // The KS4 journeys pass no list, and must keep seeing every pupil.
        var (windowId, repo) = await SeededAsync(
            NewPupil("Smith", "500001"),
            NewPupil("Smithson", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All, cypmdIdAllowList: null);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task An_empty_allow_list_suggests_nobody()
    {
        // A school whose results file has not landed holds nothing to enquire about, so the search
        // must come back empty rather than fall back to every pupil.
        var (windowId, repo) = await SeededAsync(NewPupil("Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All,
            cypmdIdAllowList: new HashSet<string>());

        Assert.Empty(results);
    }

    [Fact]
    public async Task The_allow_list_is_applied_before_the_ten_suggestion_cap()
    {
        // The cap is the last step, so filtering after it would drop the one student who does hold
        // results whenever eleven or more pupils match the typed name.
        var pupils = Enumerable.Range(1, 15)
            .Select(n => NewPupil($"Smith{n:D2}", $"5000{n:D2}"))
            .ToArray();

        var (windowId, repo) = await SeededAsync(pupils);

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All,
            cypmdIdAllowList: new HashSet<string> { "500015" });

        var only = Assert.Single(results);
        Assert.StartsWith("Smith15,", only.Label);
    }

    [Fact]
    public async Task Student_ids_are_matched_using_the_allow_lists_own_comparer()
    {
        // The results client builds the set case-insensitively; the repository must not silently
        // impose ordinal matching by copying it into a plain list.
        var (windowId, repo) = await SeededAsync(NewPupil("Smith", "a1b2"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All,
            cypmdIdAllowList: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A1B2" });

        Assert.Single(results);
    }
}
