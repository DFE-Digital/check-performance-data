using System.Reflection;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Controller behaviour tests for the search-analytics landing dashboard. The auth filter's
// anonymous-and-editor rejection branches are exercised at HTTP level by SearchAnalyticsAuthTests;
// this class covers the pass-through path — window resolution, empty-state guard, tile numbers
// against a seeded corpus — by calling the Index action directly against a real IPortalDbContext
// so the query service exercises the same SQL production runs.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsControllerTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsControllerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Attribute gate is wired at the class level with the correct section key ---

    [Fact]
    public void Controller_IsGatedBy_SearchAnalyticsSection()
    {
        var gate = typeof(SearchAnalyticsController)
            .GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.SearchAnalytics, gate!.SectionKey);
    }

    [Fact]
    public void Controller_HasClassLevelRoute_AdminSearch()
    {
        var route = typeof(SearchAnalyticsController)
            .GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("admin/Search", route!.Template);
    }

    // --- Default window is 7 days when no ?range= is supplied ---

    [Fact]
    public async Task Index_WithNoQueryString_DefaultsToSevenDayRange()
    {
        await ResetSearchEventsAsync();

        var result = await Sut().Index(range: null, from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal("7d", model.RangeKey);
        Assert.Equal(TimeSpan.FromDays(7), (model.ToUtc - model.FromUtc).Duration());
    }

    // --- Preset ranges resolve to the correct duration ---

    [Theory]
    [InlineData("24h", 24)]
    [InlineData("7d", 7 * 24)]
    [InlineData("30d", 30 * 24)]
    [InlineData("90d", 90 * 24)]
    public async Task Index_PresetRange_ResolvesToExpectedDuration(string range, int hours)
    {
        await ResetSearchEventsAsync();

        var result = await Sut().Index(range: range, from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal(range, model.RangeKey);
        Assert.Equal(TimeSpan.FromHours(hours), (model.ToUtc - model.FromUtc).Duration());
    }

    // --- Unknown ?range= falls back to the 7-day default rather than erroring ---

    [Fact]
    public async Task Index_UnknownRange_FallsBackToSevenDayDefault()
    {
        await ResetSearchEventsAsync();

        var result = await Sut().Index(range: "not-a-range", from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal("7d", model.RangeKey);
    }

    // --- Custom range is clamped to [UtcNow - 90d, UtcNow] to block abusive scans ---

    [Fact]
    public async Task Index_CustomRangeWithFarPastFrom_IsClampedToNinetyDays()
    {
        await ResetSearchEventsAsync();

        var farPast = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var future = DateTime.UtcNow.AddYears(5);

        var result = await Sut().Index(range: "custom", from: farPast, to: future, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal("custom", model.RangeKey);
        var span = model.ToUtc - model.FromUtc;
        Assert.True(span <= TimeSpan.FromDays(90.5),
            $"Expected custom range clamped to <=90d, got {span.TotalDays}d.");
    }

    // --- Tiles + tables always render regardless of row count ---

    [Fact]
    public async Task Index_WhenWindowHas100Rows_TilesMatchSeededData()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(1, 100)
            .Select(i => NewEvent(now.AddMinutes(-i), $"s-{i % 10}", "alpha", results: (i % 5 == 0) ? 0 : 3, latency: 10 + (i % 20)))
            .ToArray();
        await SeedAsync(rows);

        var result = await Sut().Index(range: "7d", from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal(100, model.TotalRowCount);
        Assert.Equal(100, model.Summary.TotalCount);
        Assert.Equal(10, model.Summary.UniqueSessions);
        Assert.NotEmpty(model.TopQueries);
    }

    [Fact]
    public async Task Index_WhenWindowHasFewerThan20Rows_StillReturnsRealSummaryAndTables()
    {
        // Regression: earlier gate swapped the tiles + tables for a "not enough data" inset
        // whenever the window held under 20 rows. Admins expect small-sample data to still
        // render — the noise on percentiles is theirs to judge.
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(1, 5)
            .Select(i => NewEvent(now.AddMinutes(-i), $"s-{i}", "alpha", results: 1, latency: 10))
            .ToArray();
        await SeedAsync(rows);

        var result = await Sut().Index(range: "7d", from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal(5, model.TotalRowCount);
        Assert.Equal(5, model.Summary.TotalCount);
        Assert.Equal(5, model.Summary.UniqueSessions);
        Assert.NotEmpty(model.TopQueries);
    }

    // --- Preset windows scope tile numbers correctly ---

    [Fact]
    public async Task Index_TwentyFourHourRange_ExcludesOlderRowsFromTiles()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        // Inside the 24h window: 25 rows.
        var inside = Enumerable.Range(1, 25)
            .Select(i => NewEvent(now.AddMinutes(-i), $"s-in-{i}", "alpha", results: 1, latency: 10));
        // Outside the 24h window: 10 older rows must NOT count.
        var outside = Enumerable.Range(1, 10)
            .Select(i => NewEvent(now.AddDays(-3).AddMinutes(-i), $"s-out-{i}", "alpha", results: 1, latency: 10));
        await SeedAsync(inside.Concat(outside).ToArray());

        var result = await Sut().Index(range: "24h", from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal(25, model.Summary.TotalCount);
    }

    // --- Bucket-size resolution: explicit sizes echo verbatim; auto-fallback follows window width ---

    [Theory]
    [InlineData("15m", "15m")]
    [InlineData("1h",  "1h")]
    [InlineData("1d",  "1d")]
    [InlineData("1w",  "1w")]
    [InlineData("1mo", "1mo")]
    public void ResolveBucketSize_ExplicitSize_EchoesKeyVerbatim(string input, string expectedKey)
    {
        var (_, key) = SearchAnalyticsController.ResolveBucketSize(input, TimeSpan.FromDays(7));
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void ResolveBucketSize_UnknownSize_FallsBackToWindowDefault()
    {
        var (size, key) = SearchAnalyticsController.ResolveBucketSize("nonsense", TimeSpan.FromHours(24));
        Assert.Equal(VolumeBucketSize.Hour, size);
        Assert.Equal("1h", key);
    }

    [Theory]
    [InlineData(24,   "1h")]  // <=48h → hour
    [InlineData(48,   "1h")]
    [InlineData(72,   "1d")]  // <=30d → day
    [InlineData(24 * 30, "1d")]
    [InlineData(24 * 31, "1w")] // >30d → week
    [InlineData(24 * 90, "1w")]
    public void ResolveBucketSize_NoInput_AutoPicksFromWindow(int windowHours, string expectedKey)
    {
        var (_, key) = SearchAnalyticsController.ResolveBucketSize(null, TimeSpan.FromHours(windowHours));
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public async Task Index_WithNoBucketParam_ModelBucketKeyMatchesAutoDefault()
    {
        await ResetSearchEventsAsync();

        var result = await Sut().Index(range: "7d", from: null, to: null, bucket: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal("1d", model.BucketKey);
    }

    [Fact]
    public async Task Index_WithExplicitBucket_ModelBucketKeyMatches()
    {
        await ResetSearchEventsAsync();

        var result = await Sut().Index(range: "7d", from: null, to: null, bucket: "1h", ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Equal("1h", model.BucketKey);
    }

    // --- AdminWide flag: dashboards use the wide layout so the sidebar-collapse frees usable space ---

    [Fact]
    public async Task Index_SetsAdminWideViewData()
    {
        await ResetSearchEventsAsync();

        var controller = Sut();
        _ = await controller.Index(range: "7d", from: null, to: null, bucket: null, ct: CancellationToken.None);

        Assert.Equal(true, controller.ViewData["AdminWide"]);
    }

    // --- Top-N tables populate on the view model when there is data ---

    [Fact]
    public async Task Index_PopulatesTopQueries_AndTopZeroResultQueries()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        // "gap" always zero-result x5; "found" always has hits x30 (dominant).
        var gap = Enumerable.Range(1, 5)
            .Select(i => NewEvent(now.AddMinutes(-i), $"s-g-{i}", "gap", results: 0, latency: 10));
        var found = Enumerable.Range(1, 30)
            .Select(i => NewEvent(now.AddMinutes(-i - 10), $"s-f-{i}", "found", results: 4, latency: 15));
        await SeedAsync(gap.Concat(found).ToArray());

        var result = await Sut().Index(range: "7d", from: null, to: null, ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.Contains(model.TopQueries, r => r.QueryNormalised == "found" && r.Count == 30);
        Assert.Contains(model.TopZeroResultQueries, r => r.QueryNormalised == "gap" && r.Count == 5);
    }

    // --- Index populates all four bucketed chart series so the interactive tiles can swap client-side ---

    [Fact]
    public async Task Index_PopulatesAllFourChartSeriesAndTopPages()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        // Seed events across the last day so every series has at least one populated bucket.
        var rows = Enumerable.Range(1, 30)
            .Select(i => NewEvent(
                now.AddMinutes(-i),
                sessionId: $"s-{i % 10}",
                queryNormalised: "alpha",
                results: (i % 4 == 0) ? 0 : 3,
                latency: 10 + (i % 20)))
            .ToArray();
        await SeedAsync(rows);

        // Add a couple of page-impression rows so TopPages has content.
        long firstEventId;
        await using (var ctx = _fixture.CreateContext())
        {
            firstEventId = ctx.SearchEvents.OrderBy(e => e.OccurredAtUtc).First().Id;
            ctx.SearchEventResults.Add(new SearchEventResult
            {
                SearchEventId = firstEventId,
                Position = 1,
                ResultKind = "page",
                ResultKey = "https://example.gov.uk/one",
                Rank = 1.0f,
            });
            ctx.SearchEventResults.Add(new SearchEventResult
            {
                SearchEventId = firstEventId,
                Position = 2,
                ResultKind = "page",
                ResultKey = "https://example.gov.uk/two",
                Rank = 0.9f,
            });
            await ctx.SaveChangesAsync();
        }

        var result = await Sut().Index(range: "24h", from: null, to: null, bucket: "1h", ct: CancellationToken.None);

        var model = AssertViewModel(result);
        Assert.NotEmpty(model.VolumeSeries);
        Assert.NotEmpty(model.UniqueSessionsSeries);
        Assert.NotEmpty(model.ZeroResultCountSeries);
        Assert.NotEmpty(model.LatencyPercentileSeries);
        Assert.NotEmpty(model.TopPages);
        Assert.True(model.TopPagesTotalCount >= model.TopPages.Count);

        // All four series ride the same spine so their bucket counts match.
        Assert.Equal(model.VolumeSeries.Count, model.UniqueSessionsSeries.Count);
        Assert.Equal(model.VolumeSeries.Count, model.ZeroResultCountSeries.Count);
        Assert.Equal(model.VolumeSeries.Count, model.LatencyPercentileSeries.Count);
    }

    // --- Helpers ---

    private SearchAnalyticsController Sut()
    {
        var context = _fixture.CreateContext();
        var query = new SearchAnalyticsQueryService(context);
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        return new SearchAnalyticsController(query, settings);
    }

    private static SearchAnalyticsIndexViewModel AssertViewModel(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<SearchAnalyticsIndexViewModel>(view.Model);
    }

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc,
        string sessionId,
        string queryNormalised,
        int results,
        int latency) => new()
        {
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            SessionId = sessionId,
            QueryRaw = queryNormalised,
            QueryNormalised = queryNormalised,
            Scope = null,
            ResultsPages = results,
            ResultsBlocks = 0,
            LatencyMs = latency,
        };

    private async Task SeedAsync(params SearchEvent[] events)
    {
        await using var context = _fixture.CreateContext();
        context.SearchEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private async Task ResetSearchEventsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
    }
}
