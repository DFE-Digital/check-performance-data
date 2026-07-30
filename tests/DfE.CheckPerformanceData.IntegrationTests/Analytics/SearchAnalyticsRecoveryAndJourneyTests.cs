using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Read-side aggregation coverage for the four query methods introduced with the zero-result
// recovery + heatmap-cell + blocks drill-in features. Every test exercises real SQL against
// the shared Postgres testcontainer so the SQL text — window predicates, CTEs, ordering,
// server-side clamps — is contract-tested end-to-end rather than mocked out.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsRecoveryAndJourneyTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsRecoveryAndJourneyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // ── GetZeroResultRecoveryStatsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetZeroResultRecoveryStats_WindowWithNoZeroResults_ReturnsAllZeros()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Sessions that all found results — nothing to recover, nothing to abandon.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-10), "s-happy-1", "q", results: 3, latency: 10),
            NewEvent(now.AddMinutes(-15), "s-happy-2", "q", results: 5, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-happy-3", "q", results: 1, latency: 10));

        var stats = await CreateService()
            .GetZeroResultRecoveryStatsAsync(from, now, CancellationToken.None);

        Assert.Equal(0, stats.SessionsWithAnyZeroResult);
        Assert.Equal(0, stats.RecoveredCount);
        Assert.Equal(0, stats.AbandonedCount);
        Assert.Equal(0, stats.SentFeedbackCount);
    }

    [Fact]
    public async Task GetZeroResultRecoveryStats_SessionWithZeroThenResult_RecoveredCountEqualsOne()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // "s-recover" hits zero then refines to a successful query in the same window.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-recover", "typo", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-25), "s-recover", "corrected", results: 4, latency: 10));

        var stats = await CreateService()
            .GetZeroResultRecoveryStatsAsync(from, now, CancellationToken.None);

        Assert.Equal(1, stats.SessionsWithAnyZeroResult);
        Assert.Equal(1, stats.RecoveredCount);
        Assert.Equal(0, stats.AbandonedCount);
        Assert.Equal(0, stats.SentFeedbackCount);
    }

    [Fact]
    public async Task GetZeroResultRecoveryStats_SessionWithOnlyZeroResults_AbandonedCountEqualsOne()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // "s-abandon" tries twice, both zero-result, never refines successfully, never
        // sends feedback. This is the textbook abandoned case the recovery card is built
        // to surface.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-abandon", "asdfasdf", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-25), "s-abandon", "qwerty", results: 0, latency: 10));

        var stats = await CreateService()
            .GetZeroResultRecoveryStatsAsync(from, now, CancellationToken.None);

        Assert.Equal(1, stats.SessionsWithAnyZeroResult);
        Assert.Equal(0, stats.RecoveredCount);
        Assert.Equal(1, stats.AbandonedCount);
        Assert.Equal(0, stats.SentFeedbackCount);
    }

    [Fact]
    public async Task GetZeroResultRecoveryStats_SessionWithZeroPlusFeedback_SentFeedbackCountEqualsOne()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // "s-feedback" fires a zero-result event AND a feedback message inside the window.
        // Recovery = 0 (no >0 event), feedback = 1, abandoned = 0 (the feedback signals
        // "not silent" so it doesn't count against the abandoned tally).
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-40), "s-feedback", "missing page", results: 0, latency: 12));
        await SeedMessagesAsync(
            NewMessage(now.AddMinutes(-20), "s-feedback", "Where's the page?"));

        var stats = await CreateService()
            .GetZeroResultRecoveryStatsAsync(from, now, CancellationToken.None);

        Assert.Equal(1, stats.SessionsWithAnyZeroResult);
        Assert.Equal(0, stats.RecoveredCount);
        Assert.Equal(0, stats.AbandonedCount);
        Assert.Equal(1, stats.SentFeedbackCount);
    }

    // ── GetZeroResultJourneysAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetZeroResultJourneys_SessionsIncluded_HaveEventsInChronologicalOrder()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Mixed events fed in a non-chronological insert order so we can pin that the
        // reader assembles the chain by occurred_at_utc, not by insertion order.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-10), "s-chain", "third",  results: 2, latency: 10),
            NewEvent(now.AddMinutes(-30), "s-chain", "first",  results: 0, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-chain", "second", results: 0, latency: 10));

        var (rows, total) = await CreateService()
            .GetZeroResultJourneysAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, total);
        var journey = Assert.Single(rows);
        Assert.Equal("s-chain", journey.SessionId);
        Assert.Equal(3, journey.Steps.Count);
        Assert.Equal("first",  journey.Steps[0].Query);
        Assert.Equal("second", journey.Steps[1].Query);
        Assert.Equal("third",  journey.Steps[2].Query);
    }

    [Fact]
    public async Task GetZeroResultJourneys_ExcludesSessionsWithNoZeroResultEvent()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Two sessions: one had a zero-result event (must appear), one only ever found
        // results (must NOT appear).
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-10), "s-with-zero",     "q", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-15), "s-with-zero",     "q", results: 2, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-always-found",  "q", results: 4, latency: 10),
            NewEvent(now.AddMinutes(-25), "s-always-found",  "q", results: 5, latency: 10));

        var (rows, total) = await CreateService()
            .GetZeroResultJourneysAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(rows);
        Assert.Equal("s-with-zero", rows[0].SessionId);
    }

    [Fact]
    public async Task GetZeroResultJourneys_OrdersByChainLengthDesc_ThenBySessionIdAsc()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Three sessions, each with a zero-result event:
        //   s-c: chain length 3
        //   s-b: chain length 1 (tie with s-a)
        //   s-a: chain length 1
        // Expected order: s-c (longest), then s-a (tie-break by id ASC), then s-b.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-c", "q1", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-25), "s-c", "q2", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-c", "q3", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-30), "s-b", "b",  results: 0, latency: 10),
            NewEvent(now.AddMinutes(-30), "s-a", "a",  results: 0, latency: 10));

        var (rows, _) = await CreateService()
            .GetZeroResultJourneysAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal("s-c", rows[0].SessionId);
        Assert.Equal("s-a", rows[1].SessionId);
        Assert.Equal("s-b", rows[2].SessionId);
    }

    [Fact]
    public async Task GetZeroResultJourneys_EventuallyRecoveredFlag_IsSetCorrectly()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Two zero-result sessions: one recovered, one didn't.
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-rec",  "typo",      results: 0, latency: 10),
            NewEvent(now.AddMinutes(-20), "s-rec",  "corrected", results: 5, latency: 10),
            NewEvent(now.AddMinutes(-30), "s-lost", "gap",       results: 0, latency: 10));

        var (rows, _) = await CreateService()
            .GetZeroResultJourneysAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        var recovered = rows.Single(j => j.SessionId == "s-rec");
        var lost      = rows.Single(j => j.SessionId == "s-lost");
        Assert.True(recovered.EventuallyRecovered);
        Assert.False(lost.EventuallyRecovered);
    }

    [Fact]
    public async Task GetZeroResultJourneys_SentFeedbackFlag_IsSetCorrectly()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-40), "s-sent",   "gap-a", results: 0, latency: 10),
            NewEvent(now.AddMinutes(-40), "s-silent", "gap-b", results: 0, latency: 10));
        await SeedMessagesAsync(
            NewMessage(now.AddMinutes(-20), "s-sent", "Send feedback"));

        var (rows, _) = await CreateService()
            .GetZeroResultJourneysAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        var sent   = rows.Single(j => j.SessionId == "s-sent");
        var silent = rows.Single(j => j.SessionId == "s-silent");
        Assert.True(sent.SentFeedback);
        Assert.False(silent.SentFeedback);
    }

    // ── GetPagedEventsInWeekdayHourBucketAsync ─────────────────────────────────────

    [Fact]
    public async Task GetPagedEventsInWeekdayHourBucket_IncludesEventsInBucket()
    {
        await TruncateAsync();

        // Anchor to a known weekday × hour tuple so the assertion is deterministic across
        // clock drift on the runner. 2026-07-27 12:30 UTC is a Monday (ISO weekday 1) in
        // the 12:00-13:00 hour bucket. Window brackets it.
        var monday1230 = new DateTime(2026, 07, 27, 12, 30, 00, DateTimeKind.Utc);
        var from = monday1230.AddHours(-1);
        var to = monday1230.AddHours(1);

        await SeedEventsAsync(
            NewEvent(monday1230,               "s-in", "q1", results: 3, latency: 10),
            NewEvent(monday1230.AddMinutes(1), "s-in", "q2", results: 4, latency: 11));

        var (rows, total) = await CreateService()
            .GetPagedEventsInWeekdayHourBucketAsync(
                from, to, weekday: 1, hour: 12, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task GetPagedEventsInWeekdayHourBucket_ExcludesEventsOutsideBucket()
    {
        await TruncateAsync();

        // Monday 12:30 (in-bucket) vs. Monday 08:30 (different hour) vs. Tuesday 12:30
        // (different weekday). Query asks for Monday hour-12: only one row must come back.
        var monday1230 = new DateTime(2026, 07, 27, 12, 30, 00, DateTimeKind.Utc);
        var monday0830 = new DateTime(2026, 07, 27, 08, 30, 00, DateTimeKind.Utc);
        var tuesday1230 = new DateTime(2026, 07, 28, 12, 30, 00, DateTimeKind.Utc);

        // Wide window covers all three events; the tuple filter (weekday=1, hour=12) does
        // the exclusion, not the window.
        var from = new DateTime(2026, 07, 26, 00, 00, 00, DateTimeKind.Utc);
        var to = new DateTime(2026, 07, 30, 00, 00, 00, DateTimeKind.Utc);

        await SeedEventsAsync(
            NewEvent(monday1230,  "s-in",  "in", results: 3, latency: 10),
            NewEvent(monday0830,  "s-out", "wrong-hour", results: 3, latency: 10),
            NewEvent(tuesday1230, "s-out", "wrong-day",  results: 3, latency: 10));

        var (rows, total) = await CreateService()
            .GetPagedEventsInWeekdayHourBucketAsync(
                from, to, weekday: 1, hour: 12, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(rows);
        Assert.Equal("s-in", rows[0].SessionId);
    }

    [Theory]
    [InlineData(0, 12)]   // weekday too low
    [InlineData(8, 12)]   // weekday too high
    [InlineData(1, -1)]   // hour too low
    [InlineData(1, 24)]   // hour too high
    public async Task GetPagedEventsInWeekdayHourBucket_OutOfBandTuple_ReturnsEmpty(int weekday, int hour)
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        await SeedEventsAsync(
            NewEvent(now.AddMinutes(-30), "s-any", "q", results: 3, latency: 10));

        var (rows, total) = await CreateService()
            .GetPagedEventsInWeekdayHourBucketAsync(
                now.AddHours(-6), now, weekday: weekday, hour: hour,
                page: 1, pageSize: 10, CancellationToken.None);

        Assert.Empty(rows);
        Assert.Equal(0, total);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(_fixture.CreateContext());

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc, string sessionId, string? queryNormalised,
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

    private static SearchMessage NewMessage(DateTime submittedAtUtc, string sessionId, string body) => new()
    {
        SessionId = sessionId,
        SubmittedAtUtc = DateTime.SpecifyKind(submittedAtUtc, DateTimeKind.Utc),
        WhatLookingFor = body,
    };

    private async Task SeedEventsAsync(params SearchEvent[] events)
    {
        await using var context = _fixture.CreateContext();
        context.SearchEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private async Task SeedMessagesAsync(params SearchMessage[] messages)
    {
        await using var context = _fixture.CreateContext();
        context.SearchMessages.AddRange(messages);
        await context.SaveChangesAsync();
    }

    private Task TruncateAsync() => SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
}
