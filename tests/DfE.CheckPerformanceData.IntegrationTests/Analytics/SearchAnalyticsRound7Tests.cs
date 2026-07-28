using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// New read-side surface behind the round-7 dashboard additions:
//   - Request-timings scatter (sampled + paged)
//   - Weekday × hour-of-day heatmap
//   - Zero-result → next-action outcome funnel (refined / feedback / silent)
//   - Week-over-week prior-window summary (drives anomaly chips)
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsRound7Tests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsRound7Tests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Request timings ----------------------------------------------------

    [Fact]
    public async Task GetRequestTimings_ReturnsRawEvents_UpToSamplingLimit()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-2);

        // Seed 50 events well inside the window, one per minute; each has a distinct
        // (session, query) pair so the scatter has enough diversity.
        var events = new List<SearchEvent>();
        for (var i = 0; i < 50; i++)
        {
            events.Add(NewEvent(from.AddMinutes(i + 5), $"s-{i}", $"q-{i}", results: (i % 3 == 0) ? 0 : 2, latency: 100 + i));
        }
        // One event WELL outside the window that must not appear.
        events.Add(NewEvent(now.AddDays(-10), "s-out", "old", results: 0, latency: 999));

        await SeedAsync(events.ToArray());

        var svc = CreateService();
        var points = await svc.GetRequestTimingsAsync(from, now, samplingLimit: 200, CancellationToken.None);

        Assert.Equal(50, points.Count);
        Assert.All(points, p => Assert.NotEqual(999, p.LatencyMs));
    }

    [Fact]
    public async Task GetRequestTimings_SamplesDown_WhenOverLimit()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-2);

        var events = new List<SearchEvent>();
        for (var i = 0; i < 100; i++)
        {
            events.Add(NewEvent(from.AddMinutes(i / 2 + 5), $"s-{i}", "q", results: 1, latency: 50 + i));
        }
        await SeedAsync(events.ToArray());

        var points = await CreateService().GetRequestTimingsAsync(from, now, samplingLimit: 25, CancellationToken.None);

        Assert.Equal(25, points.Count);
        Assert.All(points, p => Assert.True(p.OccurredAtUtc >= from && p.OccurredAtUtc < now));
    }

    [Fact]
    public async Task GetPagedRequestTimings_ReturnsPageAndTotal()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-3);

        var events = new List<SearchEvent>();
        for (var i = 0; i < 45; i++)
        {
            events.Add(NewEvent(from.AddMinutes(i * 2), $"s-{i}", $"q-{i}", results: 2, latency: 100));
        }
        await SeedAsync(events.ToArray());

        var svc = CreateService();
        var (page1, total) = await svc.GetPagedRequestTimingsAsync(from, now, page: 1, pageSize: 20, CancellationToken.None);
        var (page3, total3) = await svc.GetPagedRequestTimingsAsync(from, now, page: 3, pageSize: 20, CancellationToken.None);

        Assert.Equal(45, total);
        Assert.Equal(45, total3);
        Assert.Equal(20, page1.Count);
        Assert.Equal(5, page3.Count);
        // Newest-first: first row of page 1 is the most recent event.
        Assert.True(page1[0].OccurredAtUtc >= page1[^1].OccurredAtUtc);
    }

    // --- Weekday × hour-of-day heatmap --------------------------------------

    [Fact]
    public async Task GetSearchesByWeekdayAndHour_ReturnsFullGrid_WithZeroFilling()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);

        // Seed a small handful of events so at least one cell has count > 0 while the
        // rest are gap-filled to zero. Use a UTC-day anchor so the (weekday, hour) tuple
        // is deterministic across CI runs.
        var anchor = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, now.Day, 10, 30, 0), DateTimeKind.Utc)
            .AddDays(-1);
        await SeedAsync(
            NewEvent(anchor, "s-1", "q", results: 1, latency: 10),
            NewEvent(anchor.AddMinutes(5), "s-2", "q", results: 1, latency: 10),
            NewEvent(anchor.AddMinutes(10), "s-3", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetSearchesByWeekdayAndHourAsync(from, now, CancellationToken.None);

        // Full grid: 7 weekdays × 24 hours = 168 cells.
        Assert.Equal(168, buckets.Count);
        // Every weekday 1..7 present.
        Assert.Equal(Enumerable.Range(1, 7), buckets.Select(b => b.Weekday).Distinct().OrderBy(w => w));
        // Every hour 0..23 present.
        Assert.Equal(Enumerable.Range(0, 24), buckets.Select(b => b.Hour).Distinct().OrderBy(h => h));
        // Total count across the grid equals the events we seeded.
        Assert.Equal(3, buckets.Sum(b => b.Count));

        // ISO weekday: Monday = 1, Sunday = 7.
        var expectedWeekday = anchor.DayOfWeek switch
        {
            DayOfWeek.Sunday => 7,
            _ => (int)anchor.DayOfWeek,
        };
        var matchingCell = buckets.Single(b => b.Weekday == expectedWeekday && b.Hour == anchor.Hour);
        Assert.Equal(3, matchingCell.Count);
    }

    // --- Zero-result → outcome funnel ---------------------------------------

    [Fact]
    public async Task GetZeroResultOutcomeFunnel_ClassifiesRefinedFeedbackSilent()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // s-refined: zero-result at T0, followed by a different-query search at T+10.
        // s-feedback: zero-result at T0, no follow-up search; but a search_messages
        //   row lands at T+15 (still inside window).
        // s-silent: zero-result at T0, no follow-up search, no message.
        // s-noise: three normal searches with results — no zero-result, must NOT count.
        await SeedAsync(
            NewEvent(from.AddMinutes(30), "s-refined", "widget", results: 0, latency: 10),
            NewEvent(from.AddMinutes(40), "s-refined", "widgets", results: 5, latency: 10),

            NewEvent(from.AddMinutes(30), "s-feedback", "policy", results: 0, latency: 10),

            NewEvent(from.AddMinutes(30), "s-silent", "quiet", results: 0, latency: 10),

            NewEvent(from.AddMinutes(30), "s-noise-1", "found", results: 3, latency: 10),
            NewEvent(from.AddMinutes(35), "s-noise-2", "found", results: 3, latency: 10),
            NewEvent(from.AddMinutes(40), "s-noise-3", "found", results: 3, latency: 10));

        await SeedMessageAsync("s-feedback", from.AddMinutes(45), "we can't find our policy pages");

        var summary = await CreateService().GetZeroResultOutcomeFunnelAsync(from, now, CancellationToken.None);

        Assert.Equal(3, summary.TotalZeroResultSessions);
        Assert.Equal(1, summary.RefinedCount);
        Assert.Equal(1, summary.FeedbackCount);
        Assert.Equal(1, summary.SilentCount);
        Assert.Equal(summary.TotalZeroResultSessions, summary.RefinedCount + summary.FeedbackCount + summary.SilentCount);
        Assert.True(summary.TotalSessionsInWindow >= summary.TotalZeroResultSessions,
            $"TotalSessionsInWindow ({summary.TotalSessionsInWindow}) must be >= TotalZeroResultSessions ({summary.TotalZeroResultSessions}).");
    }

    [Fact]
    public async Task GetZeroResultOutcomeFunnel_RefineWinsOverFeedback_WhenSessionBothRefinedAndSentFeedback()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-3);

        // Same session refined AND submitted feedback → tie-break should mark it refined.
        await SeedAsync(
            NewEvent(from.AddMinutes(30), "s-both", "widget", results: 0, latency: 10),
            NewEvent(from.AddMinutes(45), "s-both", "widgets", results: 5, latency: 10));
        await SeedMessageAsync("s-both", from.AddMinutes(50), "and here's a message");

        var summary = await CreateService().GetZeroResultOutcomeFunnelAsync(from, now, CancellationToken.None);

        Assert.Equal(1, summary.TotalZeroResultSessions);
        Assert.Equal(1, summary.RefinedCount);
        Assert.Equal(0, summary.FeedbackCount);
        Assert.Equal(0, summary.SilentCount);
    }

    [Fact]
    public async Task GetZeroResultOutcomeFunnel_ReturnsZeros_WhenWindowIsEmpty()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var summary = await CreateService().GetZeroResultOutcomeFunnelAsync(
            now.AddHours(-1), now, CancellationToken.None);

        Assert.Equal(0, summary.TotalZeroResultSessions);
        Assert.Equal(0, summary.TotalSessionsInWindow);
        Assert.Equal(0, summary.RefinedCount);
        Assert.Equal(0, summary.FeedbackCount);
        Assert.Equal(0, summary.SilentCount);
    }

    // --- Prior-window deltas -------------------------------------------------

    [Fact]
    public async Task GetSummaryDeltas_ComputesPriorWindow_SameWidthEndingAtCurrentFrom()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        var currentFrom = now.AddDays(-7);
        var priorFrom = now.AddDays(-14);

        // Current window: 30 events (widely distributed).
        var current = new List<SearchEvent>();
        for (var i = 0; i < 30; i++)
        {
            current.Add(NewEvent(currentFrom.AddHours(i * 5), $"cur-{i}", "q", results: (i % 5 == 0) ? 0 : 3, latency: 100));
        }

        // Prior window: 10 events.
        var prior = new List<SearchEvent>();
        for (var i = 0; i < 10; i++)
        {
            prior.Add(NewEvent(priorFrom.AddHours(i * 5), $"pri-{i}", "q", results: 3, latency: 100));
        }

        await SeedAsync(current.Concat(prior).ToArray());

        var deltas = await CreateService().GetSummaryDeltasAsync(currentFrom, now, CancellationToken.None);

        Assert.True(deltas.Available, "Deltas should be Available: prior window is within retention.");
        Assert.Equal(10, deltas.PriorTotalCount);
        Assert.Equal(10, deltas.PriorUniqueSessions);
    }

    [Fact]
    public async Task GetSummaryDeltas_ReturnsUnavailable_WhenPriorFallsOutsideRetention()
    {
        await ResetAllAsync();

        var now = DateTime.UtcNow;
        // 60-day current window → prior window would end 60d ago and start 120d ago, i.e.
        // beyond the sink's 90-day retention floor.
        var currentFrom = now.AddDays(-60);

        var deltas = await CreateService().GetSummaryDeltasAsync(currentFrom, now, CancellationToken.None);

        Assert.False(deltas.Available, "Deltas should be Unavailable when the prior window pushes outside retention.");
    }

    // --- Helpers -------------------------------------------------------------

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(_fixture.CreateContext());

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

    private async Task SeedMessageAsync(string sessionId, DateTime submittedAtUtc, string body)
    {
        await using var context = _fixture.CreateContext();
        context.SearchMessages.Add(new SearchMessage
        {
            SessionId = sessionId,
            SubmittedAtUtc = DateTime.SpecifyKind(submittedAtUtc, DateTimeKind.Utc),
            WhatLookingFor = body,
        });
        await context.SaveChangesAsync();
    }

    private async Task ResetAllAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE search_events RESTART IDENTITY CASCADE; TRUNCATE TABLE search_messages RESTART IDENTITY CASCADE;");
    }
}
