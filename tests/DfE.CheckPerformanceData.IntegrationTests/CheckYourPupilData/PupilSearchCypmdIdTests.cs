using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// AB#304118 / #510: the KS4 merge journey's second-record page asks for "What is the CYPMD ID of the
// second duplicate record to be merged?" and hints "Start typing ID to search records", so
// PupilSuggestionFormat.Matches has to restrict that page to the CYPMD ID. These go through
// SearchPupilsAsync rather than the format class so the query parameter is proved end to end —
// config → endpoint → service → repository → match and label — and so the page-specificity claim is
// checked against the same code path every other pupil-search page uses.
[Collection(nameof(PostgresCollection))]
public sealed class PupilSearchCypmdIdTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";

    /// <summary>Derived from the CYPMD ID by <see cref="NewPupil"/>, so no two pupils share one.</summary>
    private static string UpnFor(string cypmdId) => $"A{cypmdId.PadLeft(12, '0')}";

    /// <summary>The already-chosen first record, which the match page has to exclude.</summary>
    private static readonly Guid FirstRecordId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
        string firstname, string surname, string cypmdId, bool included = true,
        string dob = "01/01/2000", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Urn = long.Parse(TestUrn),
        Laestab = TestLaestab,
        Surname = surname,
        Firstname = firstname,
        Sex = "M",
        DateOfBirth = dob,
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
        Upn = UpnFor(cypmdId),
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

    // ── US1 — matching is on the CYPMD ID and nothing else ─────────────────────

    [Fact]
    public async Task A_cypmd_id_search_returns_the_record_whose_id_begins_with_the_query()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001"),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task A_cypmd_id_search_returns_only_the_records_whose_id_begins_with_the_query()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001"),
            NewPupil("Jane", "Jones", "500002"),
            NewPupil("Jim", "Brown", "600003"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        // Asserted on the DTO's typed fields, not the label: which pupils came back is a matching
        // claim, and the label's shape is US2's business.
        Assert.Equal(["Jones", "Smith"], results.Select(r => r.Surname).Order());
    }

    [Fact]
    public async Task A_partial_cypmd_id_is_enough_to_find_the_record()
    {
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "50", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Single(results);
    }

    [Theory]
    [InlineData("Smith")]           // surname
    [InlineData("John")]            // forename
    [InlineData("John Smith")]      // split-name query
    [InlineData("A000000500001")]   // the UPN
    [InlineData("A000000")]         // a shared UPN prefix
    [InlineData("01/01/2000")]      // date of birth, display format
    public async Task A_cypmd_id_search_returns_nothing_for_anything_that_is_not_an_id(string query)
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001"),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, query, PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("  5000  ")]
    [InlineData("5000  ")]
    [InlineData(" 5000")]
    public async Task A_cypmd_id_search_ignores_surrounding_whitespace(string query)
    {
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, query, PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Single(results);
    }

    [Fact]
    public async Task An_unconfigured_page_still_matches_names()
    {
        // The parameter defaults to All, which is what keeps the merge first-record page, Remove,
        // Include and every 16-19 page on the behaviour they had before this one existed.
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All);

        Assert.Single(results);
    }

    [Fact]
    public async Task The_same_query_answers_differently_once_the_page_narrows_the_search()
    {
        // The two halves of the defect in one assertion: the query that the old search answered must
        // now come back empty on this page, while the same search with the parameter omitted — how
        // every other page calls it — still answers.
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var onTheIdPage = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);
        var onEveryOtherPage = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All);

        Assert.Empty(onTheIdPage);
        Assert.Single(onEveryOtherPage);
    }

    // ── US2 — the suggestion a narrowed search returns carries the ID ──────────

    [Fact]
    public async Task A_narrowed_search_returns_suggestions_that_show_the_cypmd_id()
    {
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Equal("Smith, John, 01/01/2000 (500001)", Assert.Single(results).Label);
    }

    [Fact]
    public async Task Every_suggestion_shows_the_id_that_distinguishes_it()
    {
        // The end-to-end statement of the fix: two duplicate records off one child, offered in one
        // dropdown for the clerk to pick between. Identical name and date of birth, so without the ID
        // these two rows would be indistinguishable.
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001"),
            NewPupil("John", "Smith", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            searchField: PupilSearchField.CypmdId);

        Assert.Equal(
            ["Smith, John, 01/01/2000 (500001)", "Smith, John, 01/01/2000 (500002)"],
            results.Select(r => r.Label).Order());
    }

    [Fact]
    public async Task An_unnarrowed_search_returns_the_same_labels_as_before()
    {
        // The other KS4 pages — the merge journey's first-record page among them — must not grow an
        // ID they never showed.
        var (windowId, repo) = await SeededAsync(NewPupil("John", "Smith", "500001"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All);

        Assert.Equal("Smith, John, 01/01/2000", Assert.Single(results).Label);
    }

    [Fact]
    public async Task The_merge_journeys_first_record_page_still_searches_by_name_and_shows_no_id()
    {
        // SC-003/SC-006: Merge_KS4June.json's select-pupil sits in the same journey as the narrowed
        // select-match-pupil, so this is where a journey-wide change would show up. It takes no field
        // value at all — the shape the page has always used.
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001"),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All);

        Assert.Equal("Smith, John, 01/01/2000", Assert.Single(results).Label);
        Assert.DoesNotContain("(", Assert.Single(results).Label);
    }

    // ── US4 — the guards that stop a pupil being merged with itself (FR-005, FR-006) ──
    // Narrowing the search must not weaken either of them. The controller-side refusals are unit
    // tests (T034); these prove the exclusion list still reaches the suggestion endpoint once the
    // query itself can only be an ID.

    [Fact]
    public async Task The_first_record_is_never_offered_as_its_own_match()
    {
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", id: FirstRecordId),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            excludeId: FirstRecordId, searchField: PupilSearchField.CypmdId);

        Assert.Equal(["Jones"], results.Select(r => r.Surname));
    }

    [Fact]
    public async Task The_first_records_own_id_returns_nothing_even_when_typed_in_full()
    {
        // FR-005 / SC-004: the strongest form of the query. If exclusion were applied by prefix or
        // dropped once the field was narrowed, this is where it would leak.
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", id: FirstRecordId),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "500001", PupilFilter.All,
            excludeId: FirstRecordId, searchField: PupilSearchField.CypmdId);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Excluding_the_first_record_does_not_hide_records_sharing_its_prefix()
    {
        // The exclusion is one record, not one ID-space: excluding 500001 must leave 500002 findable
        // by the same prefix, or the merge journey would have nothing to offer.
        var (windowId, repo) = await SeededAsync(
            NewPupil("John", "Smith", "500001", id: FirstRecordId),
            NewPupil("Jane", "Jones", "500002"));

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "5000", PupilFilter.All,
            excludeId: FirstRecordId, searchField: PupilSearchField.CypmdId);

        Assert.Single(results);
    }
}