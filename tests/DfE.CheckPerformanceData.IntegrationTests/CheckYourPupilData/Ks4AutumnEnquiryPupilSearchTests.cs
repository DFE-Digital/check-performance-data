using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(PostgresCollection))]
public sealed class Ks4AutumnEnquiryPupilSearchTests(PostgresFixture fixture)
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";
    private const string OtherLaestab = "765/4321";

    private const string AliceSmithEnquiryRow =
        "Alice, Smith, (CYPMD ID:000001, UPN:A000000000001, DOB:15/03/2010, INCLUDED)";
    private const string AliceJohnsonEnquiryRow =
        "Alice, Johnson, (CYPMD ID:000201, UPN:A000000000201, DOB:15/03/2010, NOT INCLUDED)";

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

    private static PupilRecord NewPupil(
        string firstname, string surname, string cypmdId, bool included, string laestab = TestLaestab) => new()
    {
        Id = Guid.NewGuid(),
        Urn = long.Parse(TestUrn),
        Laestab = laestab,
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
        blobClient.SetPupils(windowId, TestLaestab,
        [
            NewPupil("Alice", "Smith", "000001", included: true),
            NewPupil("Alice", "Johnson", "000201", included: false),
        ]);
        blobClient.SetPupils(windowId, OtherLaestab,
        [
            NewPupil("Zoe", "Smith", "900001", included: true, laestab: OtherLaestab),
        ]);

        return (windowId, new CheckYourPupilDataRepository(
            fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions())));
    }

    [Theory]
    [InlineData("Smith", AliceSmithEnquiryRow)]
    [InlineData("Johnson", AliceJohnsonEnquiryRow)]
    public async Task An_enquiry_search_offers_included_and_not_included_pupils_in_the_enquiry_row(
        string query, string expectedRow)
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, query, PupilFilter.All,
            style: PupilSuggestionStyle.ResultsEnquiry);

        Assert.Equal(expectedRow, Assert.Single(results).Label);
    }

    [Theory]
    [InlineData("A000000000201")]
    [InlineData("0002")]
    public async Task An_enquiry_search_finds_a_pupil_by_upn_or_cypmd_id(string query)
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, query, PupilFilter.All,
            style: PupilSuggestionStyle.ResultsEnquiry);

        Assert.Equal(AliceJohnsonEnquiryRow, Assert.Single(results).Label);
    }

    [Fact]
    public async Task An_enquiry_search_finds_each_pupil_with_the_date_of_birth()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "15/03/2010", PupilFilter.All,
            style: PupilSuggestionStyle.ResultsEnquiry);

        Assert.Equal(
            new[] { AliceJohnsonEnquiryRow, AliceSmithEnquiryRow },
            results.Select(r => r.Label).ToArray());
    }

    [Fact]
    public async Task An_enquiry_search_offers_no_pupil_of_another_school()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Zoe", PupilFilter.All,
            style: PupilSuggestionStyle.ResultsEnquiry);

        Assert.Empty(results);
    }

    [Fact]
    public async Task An_enquiry_search_limited_to_pupils_with_results_keeps_the_enquiry_row()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Alice", PupilFilter.All,
            cypmdIdAllowList: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "000201" },
            style: PupilSuggestionStyle.ResultsEnquiry);

        Assert.Equal(AliceJohnsonEnquiryRow, Assert.Single(results).Label);
    }

    [Fact]
    public async Task The_same_search_with_no_style_keeps_the_ks4_row()
    {
        var (windowId, repo) = await SeededAsync();

        var results = await repo.SearchPupilsAsync(
            windowId, TestLaestab, TestUrn, "Smith", PupilFilter.All);

        Assert.Equal(
            "Smith, Alice, (CYPMD ID: 000001, UPN: A000000000001, DOB: 15/03/2010, Status: INCLUDED)",
            Assert.Single(results).Label);
    }
}
