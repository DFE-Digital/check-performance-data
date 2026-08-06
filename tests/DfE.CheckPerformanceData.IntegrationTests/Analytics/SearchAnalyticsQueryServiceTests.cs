using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Read-side aggregation over search_events. The Postgres server does the heavy lifting:
// a single window query returns total / distinct-sessions / zero-count / p95 for the tiles,
// grouped queries return the top-N normalised queries and top-N zero-result queries, and a
// LINQ read returns the newest event for a given session id (used by the feedback form's
// pre-fill helper). Every window bound is a server-owned DateTime and every SQL parameter
// binds via Npgsql so the read path has no injection surface.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsQueryServiceTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsQueryServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Summary counts (searches, unique sessions, zero-result rate, p95 latency) match hand-calculated values ---

    [Fact]
    public async Task GetSummary_ReturnsHandCalculatedValues_ForA24HourWindow()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-24);

        // 10 events inside the 24h window: 3 distinct session ids, 4 zero-result rows, latencies
        // {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}ms — hand-calculated p95 = 95.5ms (percentile_cont
        // interpolation between the 9th and 10th ordered values).
        var inside = new[]
        {
            NewEvent(now.AddMinutes(-5), "s-1", "how do i submit", results: 3, latency: 10),
            NewEvent(now.AddMinutes(-10), "s-1", "how do i submit", results: 2, latency: 20),
            NewEvent(now.AddMinutes(-15), "s-2", "empty query", results: 0, latency: 30),
            NewEvent(now.AddMinutes(-20), "s-2", "empty query", results: 0, latency: 40),
            NewEvent(now.AddMinutes(-25), "s-3", "another", results: 5, latency: 50),
            NewEvent(now.AddMinutes(-30), "s-3", "another", results: 0, latency: 60),
            NewEvent(now.AddMinutes(-35), "s-3", "how do i submit", results: 1, latency: 70),
            NewEvent(now.AddMinutes(-40), "s-1", "another", results: 4, latency: 80),
            NewEvent(now.AddMinutes(-45), "s-2", "how do i submit", results: 0, latency: 90),
            NewEvent(now.AddMinutes(-50), "s-1", "another", results: 2, latency: 100),
        };
        // Two events OLDER than the window — must not be counted.
        var older = new[]
        {
            NewEvent(now.AddHours(-48), "s-old", "old query", results: 1, latency: 500),
            NewEvent(now.AddHours(-72), "s-old", "old query", results: 0, latency: 999),
        };
        await SeedAsync(inside.Concat(older).ToArray());

        var service = CreateService();
        var summary = await service.GetSummaryAsync(from, now, CancellationToken.None);

        Assert.Equal(10, summary.TotalCount);
        Assert.Equal(3, summary.UniqueSessions);
        Assert.Equal(40.0, summary.ZeroResultRatePercent, precision: 2);
        Assert.InRange(summary.P95LatencyMs, 90, 100);
    }

    [Fact]
    public async Task GetSummary_ZeroResultRate_IsPercentageBetweenZeroAndOneHundred()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-1);

        await SeedAsync(
            NewEvent(now.AddMinutes(-10), "s-1", "q", results: 0, latency: 5),
            NewEvent(now.AddMinutes(-20), "s-1", "q", results: 0, latency: 5),
            NewEvent(now.AddMinutes(-30), "s-1", "q", results: 1, latency: 5),
            NewEvent(now.AddMinutes(-40), "s-1", "q", results: 1, latency: 5));

        var service = CreateService();
        var summary = await service.GetSummaryAsync(from, now, CancellationToken.None);

        Assert.InRange(summary.ZeroResultRatePercent, 0.0, 100.0);
        Assert.Equal(50.0, summary.ZeroResultRatePercent, precision: 2);
    }

    [Fact]
    public async Task GetSummary_EmptyWindow_ReturnsZeros()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var summary = await CreateService()
            .GetSummaryAsync(now.AddHours(-1), now, CancellationToken.None);

        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.UniqueSessions);
        Assert.Equal(0.0, summary.ZeroResultRatePercent);
        Assert.Equal(0, summary.P95LatencyMs);
    }

    // --- Top queries returns the most-searched normalised queries in DESC order ---

    [Fact]
    public async Task GetTopQueries_ReturnsMostSearchedNormalisedQueriesInDescOrder()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);

        // "alpha" x5, "bravo" x3, "charlie" x1. "delta" is outside the window and must NOT appear.
        var alpha = Enumerable.Range(0, 5)
            .Select(i => NewEvent(now.AddDays(-1).AddMinutes(i), "s-a", "alpha", results: 2, latency: 10));
        var bravo = Enumerable.Range(0, 3)
            .Select(i => NewEvent(now.AddDays(-2).AddMinutes(i), "s-b", "bravo", results: 0, latency: 10));
        var charlie = new[] { NewEvent(now.AddDays(-3), "s-c", "charlie", results: 1, latency: 10) };
        var delta = new[] { NewEvent(now.AddDays(-30), "s-d", "delta", results: 1, latency: 10) };
        await SeedAsync(alpha.Concat(bravo).Concat(charlie).Concat(delta).ToArray());

        var top = await CreateService().GetTopQueriesAsync(from, now, limit: 10, CancellationToken.None);

        Assert.Equal(3, top.Count);
        Assert.Equal("alpha", top[0].QueryNormalised);
        Assert.Equal(5, top[0].Count);
        Assert.Equal(0, top[0].ZeroResultCount);
        Assert.Equal("bravo", top[1].QueryNormalised);
        Assert.Equal(3, top[1].Count);
        Assert.Equal(3, top[1].ZeroResultCount);
        Assert.Equal("charlie", top[2].QueryNormalised);
    }

    [Fact]
    public async Task GetTopQueries_HonoursLimit()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        for (var i = 0; i < 15; i++)
        {
            await SeedAsync(NewEvent(now.AddMinutes(-i), $"s-{i}", $"q-{i:00}", results: 1, latency: 10));
        }

        var top = await CreateService().GetTopQueriesAsync(now.AddHours(-1), now, limit: 5, CancellationToken.None);

        Assert.Equal(5, top.Count);
    }

    [Fact]
    public async Task GetTopQueries_IgnoresRowsWithNullQueryNormalised()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;

        await SeedAsync(
            NewEvent(now.AddMinutes(-5), "s-1", queryNormalised: null, results: 1, latency: 10),
            NewEvent(now.AddMinutes(-10), "s-2", "real-query", results: 1, latency: 10));

        var top = await CreateService().GetTopQueriesAsync(now.AddHours(-1), now, limit: 10, CancellationToken.None);

        Assert.Single(top);
        Assert.Equal("real-query", top[0].QueryNormalised);
    }

    // --- Top zero-result queries filters to zero_results = true ---

    [Fact]
    public async Task GetTopZeroResultQueries_OnlyIncludesZeroResultRows()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);

        // "gap" x3 zero-results, "found" x2 with results (must NOT appear), "empty" x1 zero-result.
        var gap = Enumerable.Range(0, 3)
            .Select(i => NewEvent(now.AddDays(-1).AddMinutes(i), "s-g", "gap", results: 0, latency: 10));
        var found = Enumerable.Range(0, 2)
            .Select(i => NewEvent(now.AddDays(-1).AddMinutes(i + 10), "s-f", "found", results: 3, latency: 10));
        var empty = new[] { NewEvent(now.AddDays(-2), "s-e", "empty", results: 0, latency: 10) };
        await SeedAsync(gap.Concat(found).Concat(empty).ToArray());

        var top = await CreateService()
            .GetTopZeroResultQueriesAsync(from, now, limit: 10, CancellationToken.None);

        Assert.Equal(2, top.Count);
        Assert.Equal("gap", top[0].QueryNormalised);
        Assert.Equal(3, top[0].Count);
        Assert.Equal(3, top[0].ZeroResultCount);
        Assert.Equal("empty", top[1].QueryNormalised);
        Assert.DoesNotContain(top, r => r.QueryNormalised == "found");
    }

    // --- GetRowCountAsync is the empty-state guard used by the landing view ---

    [Fact]
    public async Task GetRowCount_CountsOnlyRowsInsideTheWindow()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 5; i++)
            await SeedAsync(NewEvent(now.AddMinutes(-i), $"s-in-{i}", "q", results: 1, latency: 5));
        await SeedAsync(NewEvent(now.AddDays(-30), "s-out", "q", results: 1, latency: 5));

        // Window is [now - 1h, now) — every seeded "inside" row is strictly earlier than 'now'.
        var count = await CreateService().GetRowCountAsync(now.AddHours(-1), now, CancellationToken.None);

        Assert.Equal(5, count);
    }

    // --- GetLatestSearchForSessionAsync returns the newest event for the given session ---

    [Fact]
    public async Task GetLatestSearchForSession_ReturnsNewestRowForThatSession()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;

        await SeedAsync(
            NewEvent(now.AddMinutes(-30), "session-alpha", "old-query", results: 4, latency: 10),
            NewEvent(now.AddMinutes(-10), "session-alpha", "newer-query", results: 7, latency: 20),
            NewEvent(now.AddMinutes(-5),  "session-beta",  "beta-query", results: 2, latency: 30));

        var latest = await CreateService()
            .GetLatestSearchForSessionAsync("session-alpha", CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("newer-query", latest!.QueryRaw);
        Assert.Equal("newer-query", latest.QueryNormalised);
        Assert.Equal(7, latest.ResultsTotal);
    }

    [Fact]
    public async Task GetLatestSearchForSession_ReturnsNull_ForUnknownSessionId()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;
        await SeedAsync(NewEvent(now.AddMinutes(-5), "session-real", "q", results: 1, latency: 5));

        var latest = await CreateService()
            .GetLatestSearchForSessionAsync("no-such-session", CancellationToken.None);

        Assert.Null(latest);
    }

    // --- GetLatestSearchForSessionAtOrBeforeAsync — bounds to a supplied timestamp ---

    [Fact]
    public async Task GetLatestSearchForSessionAtOrBefore_ReturnsNewestRowAtOrBeforeTheBound()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;
        var boundary = now.AddMinutes(-15);

        await SeedAsync(
            // Older than the bound — a candidate for the "newest at or before" answer.
            NewEvent(now.AddMinutes(-40), "s-bounded", "very-old-query", results: 4, latency: 10),
            // Exactly at the bound — included by the "at or before" contract.
            NewEvent(boundary,              "s-bounded", "at-boundary",    results: 3, latency: 12),
            // Newer than the bound — must NOT be returned even though it is the newest overall.
            NewEvent(now.AddMinutes(-5),  "s-bounded", "post-boundary",  results: 9, latency: 25));

        var latest = await CreateService()
            .GetLatestSearchForSessionAtOrBeforeAsync("s-bounded", boundary, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("at-boundary", latest!.QueryRaw);
        Assert.Equal(3, latest.ResultsTotal);
    }

    [Fact]
    public async Task GetLatestSearchForSessionAtOrBefore_ReturnsNull_WhenNoRowBelowBound()
    {
        await ResetSearchEventsAsync();
        var now = DateTime.UtcNow;

        // Only events strictly newer than the bound exist for this session — the reader
        // must return null rather than clamping to any later row.
        await SeedAsync(
            NewEvent(now.AddMinutes(-5), "s-none-before", "future-query", results: 1, latency: 5));

        var latest = await CreateService()
            .GetLatestSearchForSessionAtOrBeforeAsync(
                "s-none-before", now.AddMinutes(-10), CancellationToken.None);

        Assert.Null(latest);
    }

    // --- Windows scope correctly: only rows inside [from, to) contribute ---

    [Fact]
    public async Task GetSummary_ExcludesRowsOlderThanFrom_AndYoungerThanTo()
    {
        await ResetSearchEventsAsync();

        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);
        var to = now.AddDays(-1);

        await SeedAsync(
            NewEvent(now.AddDays(-30), "s-far-past", "q", results: 1, latency: 5),
            NewEvent(now.AddDays(-3),  "s-inside",   "q", results: 1, latency: 5),
            NewEvent(now.AddHours(-1), "s-future",   "q", results: 1, latency: 5));

        var summary = await CreateService().GetSummaryAsync(from, to, CancellationToken.None);

        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(1, summary.UniqueSessions);
    }

    // --- Helpers ---

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(
                _fixture.CreateContext(),
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc,
        string sessionId,
        string? queryNormalised,
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
