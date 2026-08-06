using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Controller behaviour tests for the three actions added with the recovery/heatmap/blocks
// drill-in work: HeatmapCell (weekday+hour grid), ZeroResultJourneys (per-session
// refinement chains + recovery summary), and Blocks (block-only cousin of the Pages
// drill-in). Each test drives the action against a real query service + Postgres
// testcontainer so route model binding, defensive clamps, and empty-state renders exercise
// the same code path the browser hits.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsNewDrillInActionsTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsNewDrillInActionsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // ── HeatmapCell ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HeatmapCell_ValidWeekdayAndHour_RendersHeatmapCellViewModel()
    {
        await TruncateAsync();

        // Monday 2026-07-27 12:30 UTC is ISO weekday 1, hour 12.
        var monday1230 = new DateTime(2026, 07, 27, 12, 30, 00, DateTimeKind.Utc);
        var from = monday1230.AddHours(-1);
        var to = monday1230.AddHours(1);

        await SeedEventsAsync(NewEvent(monday1230, "s-mon", "q", results: 3, latency: 10));

        var result = await Sut().HeatmapCell(
            range: "custom", from: from, to: to,
            weekday: 1, hour: 12, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsHeatmapCellViewModel>(view.Model);
        Assert.Equal(1, model.Weekday);
        Assert.Equal(12, model.Hour);
        Assert.Equal("Monday", model.WeekdayLabel);
        Assert.Equal("12:00–13:00 UTC", model.HourLabel);
        Assert.Equal(1, model.TotalCount);
        Assert.Single(model.Rows);
    }

    [Theory]
    [InlineData(0, 12)]      // weekday too low
    [InlineData(8, 12)]      // weekday too high
    [InlineData(1, -1)]      // hour too low
    [InlineData(1, 24)]      // hour too high
    public async Task HeatmapCell_OutOfBandTuple_ReturnsNotFound(int weekdayIn, int hourIn)
    {
        await TruncateAsync();

        var result = await Sut().HeatmapCell(
            range: "7d", from: null, to: null,
            weekday: weekdayIn, hour: hourIn, page: 1, ct: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task HeatmapCell_EmptyWindow_RendersZeroTotalCount()
    {
        await TruncateAsync();

        var result = await Sut().HeatmapCell(
            range: "7d", from: null, to: null,
            weekday: 3, hour: 9, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsHeatmapCellViewModel>(view.Model);
        Assert.Equal(0, model.TotalCount);
        Assert.Empty(model.Rows);
    }

    // ── ZeroResultJourneys ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroResultJourneys_WithZeroResultSessions_RendersRecoveryStatsAndRows()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-rec",  "typo",    results: 0, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-rec",  "corrected", results: 4, latency: 10),
            NewEvent(now.AddMinutes(-30), "s-lost", "gap",     results: 0, latency: 10));

        var result = await Sut().ZeroResultJourneys(
            range: "24h", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsZeroResultJourneysViewModel>(view.Model);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(2, model.RecoveryStats.SessionsWithAnyZeroResult);
        Assert.Equal(1, model.RecoveryStats.RecoveredCount);
        Assert.Equal(1, model.RecoveryStats.AbandonedCount);
    }

    [Fact]
    public async Task ZeroResultJourneys_SuccessBeforeZero_IsNotCountedAsRecovered()
    {
        // Regression: recovery-stats used to count ANY successful event in the window as
        // recovery, so success-at-10:00 + zero-at-11:00 was marked recovered even though
        // the session's last action was still a zero-result. The CTE now enforces that
        // the successful event must land AFTER the session's first zero-result, matching
        // the sibling funnel CTE.
        await TruncateAsync();

        var now = DateTime.UtcNow;
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-mid",  "specific",   results: 5, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-mid",  "garbage",    results: 0, latency: 10));

        var result = await Sut().ZeroResultJourneys(
            range: "24h", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsZeroResultJourneysViewModel>(view.Model);
        Assert.Equal(1, model.RecoveryStats.SessionsWithAnyZeroResult);
        Assert.Equal(0, model.RecoveryStats.RecoveredCount);
        Assert.Equal(1, model.RecoveryStats.AbandonedCount);
        // The session's row must not carry Recovered=true either.
        var session = Assert.Single(model.Rows);
        Assert.False(session.EventuallyRecovered);
    }

    [Fact]
    public async Task ZeroResultJourneys_EmptyWindow_RendersEmptyRowsAndZeroStats()
    {
        await TruncateAsync();

        var result = await Sut().ZeroResultJourneys(
            range: "24h", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsZeroResultJourneysViewModel>(view.Model);
        Assert.Equal(0, model.TotalCount);
        Assert.Empty(model.Rows);
        Assert.Equal(0, model.RecoveryStats.SessionsWithAnyZeroResult);
        Assert.Equal(0, model.RecoveryStats.RecoveredCount);
        Assert.Equal(0, model.RecoveryStats.AbandonedCount);
        Assert.Equal(0, model.RecoveryStats.SentFeedbackCount);
    }

    // ── Blocks drill-in ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlocksAction_ReturnsPagesDrillInViewModel_WithBlockKeysOnly()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var parent = NewEvent(now.AddMinutes(-30), "s-block", "q", results: 3, latency: 10);
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.Add(parent);
            await ctx.SaveChangesAsync();

            ctx.SearchEventResults.Add(NewResult(parent.Id, "block-hero", "block", position: 1));
            ctx.SearchEventResults.Add(NewResult(parent.Id, "block-nav",  "block", position: 2));
            ctx.SearchEventResults.Add(NewResult(parent.Id, "https://example.gov.uk/skip", "page", position: 3));
            await ctx.SaveChangesAsync();
        }

        var result = await Sut().Blocks(
            range: "24h", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        // Regression: the action previously returned Pages.cshtml, so the drill-in
        // rendered with the wrong title/H1/copy and the pager linked back to /Pages.
        Assert.Equal("~/Views/Admin/Search/Blocks.cshtml", view.ViewName);
        var model = Assert.IsType<SearchAnalyticsPagesDrillInViewModel>(view.Model);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(2, model.Rows.Count);
        Assert.All(model.Rows, r => Assert.StartsWith("block-", r.ResultKey));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private SearchAnalyticsController Sut()
    {
        var context = _fixture.CreateContext();
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        return new SearchAnalyticsController(query, settings);
    }

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc, string sessionId, string queryNormalised,
        int results, int latency) => new()
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

    private static SearchEventResult NewResult(long searchEventId, string resultKey, string kind, int position) => new()
    {
        SearchEventId = searchEventId,
        Position = position,
        ResultKind = kind,
        ResultKey = resultKey,
        Rank = 1.0f,
    };

    private async Task SeedEventsAsync(params SearchEvent[] events)
    {
        await using var context = _fixture.CreateContext();
        context.SearchEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private Task TruncateAsync() => SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
}
