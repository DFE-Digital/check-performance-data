using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Application.RulesConfig;
using Microsoft.EntityFrameworkCore;
using MetricEntity = DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// Read-side aggregation over queue_metrics_events: time-bucket throughput with gap-fill,
// per-stage dwell, decision-mix, per-message journey, and deploy markers. Real Postgres so
// the date_trunc + generate_series SQL is exercised against the actual engine.
[Collection(nameof(PostgresCollection))]
public sealed class MetricsQueryTests
{
    private const string RulesEngineQueue = "rules-engine";
    private const string ZendeskQueue = "zendesk";

    private readonly PostgresFixture _fixture;

    public MetricsQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Per-minute throughput gap-fills empty minutes with zero and counts populated ones ---

    [Fact]
    public async Task GetThroughput_PerMinute_GapFillsEmptyMinutesAndCountsPopulated()
    {
        await ResetMetricsAsync();

        // Anchor on a whole minute so bucket boundaries are deterministic.
        var anchor = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Minute 0: two events. Minute 1: none. Minute 2: one event.
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "REF-1", anchor.AddSeconds(5)),
            Metric(RulesEngineQueue, "RulesEvaluated", "REF-2", anchor.AddSeconds(40)),
            Metric(RulesEngineQueue, "RulesEvaluated", "REF-3", anchor.AddMinutes(2).AddSeconds(10)));

        var service = CreateService();
        var from = anchor;
        var to = anchor.AddMinutes(3);

        var buckets = await service.GetThroughputAsync(RulesEngineQueue, ThroughputGranularity.Minute, from, to);

        // Three one-minute buckets across [from, to): 0, 1, 2.
        Assert.Equal(3, buckets.Count);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal(0, buckets[1].Count); // gap-filled
        Assert.Equal(1, buckets[2].Count);
        Assert.Equal(anchor, buckets[0].BucketStartUtc);
        Assert.Equal(anchor.AddMinutes(1), buckets[1].BucketStartUtc);
    }

    // --- An unaligned (mid-bucket) from still joins the calendar-aligned counts ---

    [Fact]
    public async Task GetThroughput_UnalignedFrom_StillCountsPopulatedBuckets()
    {
        await ResetMetricsAsync();

        // The hour the events live in.
        var hour = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc);

        // Two events inside that hour.
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "U-1", hour.AddMinutes(10)),
            Metric(RulesEngineQueue, "RulesEvaluated", "U-2", hour.AddMinutes(40)));

        var service = CreateService();

        // Production passes a sub-bucket-precision 'from' (e.g. now - 24h). Emulate that with a
        // 'from' deliberately 17 seconds past the hour boundary. The counted side buckets to the
        // calendar hour, so an unaligned spine would never join and the count would be zero.
        var from = hour.AddSeconds(17);
        var to = from.AddHours(1);

        var buckets = await service.GetThroughputAsync(
            RulesEngineQueue, ThroughputGranularity.Hour, from, to);

        // The two events fall in the hour bucket the unaligned window overlaps; the total across
        // the returned buckets must be 2, not 0.
        Assert.Equal(2, buckets.Sum(b => b.Count));
    }

    // --- Events that just happened land in the bucket containing 'to' and must be counted ---

    [Fact]
    public async Task GetThroughput_IncludesTheBucketContainingTo()
    {
        await ResetMetricsAsync();

        // The dashboard queries (now - 24h, now): events recorded moments ago sit in the
        // same calendar bucket as 'to'. A spine that stops a full step before 'to' drops
        // that in-progress bucket, so fresh traffic reads as zero until the hour rolls over.
        var to = new DateTime(2026, 1, 2, 9, 3, 57, DateTimeKind.Utc);
        var from = to.AddHours(-24);

        await SeedMetricsAsync(
            Metric(ZendeskQueue, "TicketCreated", "L-1", to.AddSeconds(-30)),
            Metric(ZendeskQueue, "TicketCreated", "L-2", to.AddSeconds(-20)),
            Metric(ZendeskQueue, "TicketCreated", "L-3", to.AddMinutes(-90)));

        var service = CreateService();
        var buckets = await service.GetThroughputAsync(
            ZendeskQueue, ThroughputGranularity.Hour, from, to);

        Assert.Equal(3, buckets.Sum(b => b.Count));
    }

    // --- A different queue's events are excluded from the per-queue throughput ---

    [Fact]
    public async Task GetThroughput_ScopesToTheNamedQueue()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "R-1", anchor.AddSeconds(5)),
            Metric(ZendeskQueue, "TicketCreated", "Z-1", anchor.AddSeconds(6)),
            Metric(ZendeskQueue, "TicketCreated", "Z-2", anchor.AddSeconds(7)));

        var service = CreateService();
        var buckets = await service.GetThroughputAsync(
            ZendeskQueue, ThroughputGranularity.Minute, anchor, anchor.AddMinutes(1));

        var single = Assert.Single(buckets);
        Assert.Equal(2, single.Count);
    }

    // --- Five-minute buckets group via floor arithmetic ---

    [Fact]
    public async Task GetThroughput_FiveMinute_BucketsByFloorArithmetic()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc);

        // 0-5 min bucket: two events (at +1 and +4 min). 5-10 min bucket: one (at +7 min).
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "F-1", anchor.AddMinutes(1)),
            Metric(RulesEngineQueue, "RulesEvaluated", "F-2", anchor.AddMinutes(4)),
            Metric(RulesEngineQueue, "RulesEvaluated", "F-3", anchor.AddMinutes(7)));

        var service = CreateService();
        var buckets = await service.GetThroughputAsync(
            RulesEngineQueue, ThroughputGranularity.FiveMinute, anchor, anchor.AddMinutes(10));

        Assert.Equal(2, buckets.Count);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal(anchor, buckets[0].BucketStartUtc);
        Assert.Equal(1, buckets[1].Count);
        Assert.Equal(anchor.AddMinutes(5), buckets[1].BucketStartUtc);
    }

    // --- Ten-minute buckets group via floor arithmetic ---

    [Fact]
    public async Task GetThroughput_TenMinute_BucketsByFloorArithmetic()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 4, 8, 0, 0, DateTimeKind.Utc);

        // 0-10 bucket: one event (at +3). 10-20 bucket: two (at +12, +18).
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "T-1", anchor.AddMinutes(3)),
            Metric(RulesEngineQueue, "RulesEvaluated", "T-2", anchor.AddMinutes(12)),
            Metric(RulesEngineQueue, "RulesEvaluated", "T-3", anchor.AddMinutes(18)));

        var service = CreateService();
        var buckets = await service.GetThroughputAsync(
            RulesEngineQueue, ThroughputGranularity.TenMinute, anchor, anchor.AddMinutes(20));

        Assert.Equal(2, buckets.Count);
        Assert.Equal(1, buckets[0].Count);
        Assert.Equal(anchor, buckets[0].BucketStartUtc);
        Assert.Equal(2, buckets[1].Count);
        Assert.Equal(anchor.AddMinutes(10), buckets[1].BucketStartUtc);
    }

    // --- Decision-mix returns counts keyed by decision status ---

    [Fact]
    public async Task GetDecisionMix_ReturnsCountsPerDecisionStatus()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "D-1", anchor.AddSeconds(1), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "D-2", anchor.AddSeconds(2), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "D-3", anchor.AddSeconds(3), decision: "AutoRejected"),
            Metric(RulesEngineQueue, "RulesEvaluated", "D-4", anchor.AddSeconds(4), decision: "RequiresScrutiny"));

        var service = CreateService();
        var mix = await service.GetDecisionMixAsync(anchor, anchor.AddMinutes(1));

        Assert.Equal(2, mix.Single(m => m.DecisionStatus == "AutoApproved").Count);
        Assert.Equal(1, mix.Single(m => m.DecisionStatus == "AutoRejected").Count);
        Assert.Equal(1, mix.Single(m => m.DecisionStatus == "RequiresScrutiny").Count);
    }

    // --- Decision-mix-over-time buckets counts per status and gap-fills empty cells to zero ---

    [Fact]
    public async Task GetDecisionMixOverTime_BucketsCountsPerStatus_AndGapFills()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);

        // Minute 0: two approvals and a rejection. Minute 1: nothing. Minute 2: one approval
        // and one scrutiny. An undecided event sits in minute 0 and must not be counted.
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "M-1", anchor.AddSeconds(1), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "M-2", anchor.AddSeconds(2), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "M-3", anchor.AddSeconds(3), decision: "AutoRejected"),
            Metric(RulesEngineQueue, "RulesEvaluated", "M-4", anchor.AddSeconds(4)),
            Metric(RulesEngineQueue, "RulesEvaluated", "M-5", anchor.AddMinutes(2).AddSeconds(5), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "M-6", anchor.AddMinutes(2).AddSeconds(6), decision: "RequiresScrutiny"));

        var service = CreateService();
        var buckets = await service.GetDecisionMixOverTimeAsync(
            ThroughputGranularity.Minute, anchor, anchor.AddMinutes(3));

        // Three statuses present in the window x three one-minute buckets = nine cells.
        Assert.Equal(9, buckets.Count);

        int Count(DateTime bucket, string status) => buckets
            .Single(b => b.BucketStartUtc == bucket && b.DecisionStatus == status).Count;

        Assert.Equal(2, Count(anchor, "AutoApproved"));
        Assert.Equal(1, Count(anchor, "AutoRejected"));
        Assert.Equal(0, Count(anchor, "RequiresScrutiny"));
        Assert.Equal(0, Count(anchor.AddMinutes(1), "AutoApproved")); // gap-filled
        Assert.Equal(1, Count(anchor.AddMinutes(2), "AutoApproved"));
        Assert.Equal(1, Count(anchor.AddMinutes(2), "RequiresScrutiny"));
    }

    // --- An unaligned (mid-bucket) from still joins the calendar-aligned decision counts ---

    [Fact]
    public async Task GetDecisionMixOverTime_UnalignedFrom_StillCountsPopulatedBuckets()
    {
        await ResetMetricsAsync();
        var hour = new DateTime(2026, 1, 11, 13, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "MU-1", hour.AddMinutes(10), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "MU-2", hour.AddMinutes(40), decision: "AutoRejected"));

        var service = CreateService();

        // Production passes a sub-bucket-precision 'from' (now - window). Emulate with a 'from'
        // 17 seconds past the hour: the counted side buckets to the calendar hour, so an
        // unaligned spine would never join and every cell would gap-fill to zero.
        var from = hour.AddSeconds(17);
        var buckets = await service.GetDecisionMixOverTimeAsync(
            ThroughputGranularity.Hour, from, from.AddHours(1));

        Assert.Equal(2, buckets.Sum(b => b.Count));
    }

    // --- Decisions made moments ago land in the bucket containing 'to' and must be counted ---

    [Fact]
    public async Task GetDecisionMixOverTime_IncludesTheBucketContainingTo()
    {
        await ResetMetricsAsync();

        // Same shape as the dashboard call: (now - 24h, now), decisions recorded seconds
        // before 'to'. The in-progress bucket must appear in the series.
        var to = new DateTime(2026, 1, 11, 9, 3, 57, DateTimeKind.Utc);
        var from = to.AddHours(-24);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "ML-1", to.AddSeconds(-30), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "RulesEvaluated", "ML-2", to.AddSeconds(-20), decision: "AutoRejected"));

        var service = CreateService();
        var buckets = await service.GetDecisionMixOverTimeAsync(
            ThroughputGranularity.Hour, from, to);

        Assert.Equal(2, buckets.Sum(b => b.Count));
    }

    // --- A window with no decided events returns an empty series, not a spine of zeros ---

    [Fact]
    public async Task GetDecisionMixOverTime_NoDecisions_ReturnsEmpty()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "MN-1", anchor.AddSeconds(1)));

        var service = CreateService();
        var buckets = await service.GetDecisionMixOverTimeAsync(
            ThroughputGranularity.Minute, anchor, anchor.AddMinutes(2));

        Assert.Empty(buckets);
    }

    // --- The decision series carries the same abusive-aggregation range guard ---

    [Fact]
    public async Task GetDecisionMixOverTime_RejectsOverWideRange()
    {
        await ResetMetricsAsync();
        var from = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.GetDecisionMixOverTimeAsync(ThroughputGranularity.Second, from, to));
    }

    // --- Dwell-by-stage returns the average latency for each stage ---

    [Fact]
    public async Task GetDwellByStage_ReturnsAverageLatencyPerStage()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 6, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "L-1", anchor.AddSeconds(1), latencyMs: 100),
            Metric(RulesEngineQueue, "RulesEvaluated", "L-2", anchor.AddSeconds(2), latencyMs: 300),
            Metric(ZendeskQueue, "TicketCreated", "L-3", anchor.AddSeconds(3), latencyMs: 50));

        var service = CreateService();
        var dwell = await service.GetDwellByStageAsync(anchor, anchor.AddMinutes(1));

        Assert.Equal(200, dwell.Single(d => d.Stage == "RulesEvaluated").AverageLatencyMs);
        Assert.Equal(50, dwell.Single(d => d.Stage == "TicketCreated").AverageLatencyMs);
    }

    // --- A per-message journey returns its events ordered by recorded_at_utc ---

    [Fact]
    public async Task GetJourney_ReturnsEventsForReferenceOrderedByTime()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 7, 10, 0, 0, DateTimeKind.Utc);

        // Seed out of order; expect them back in chronological order for the one reference.
        await SeedMetricsAsync(
            Metric(ZendeskQueue, "TicketCreated", "JOURNEY-1", anchor.AddSeconds(30)),
            Metric(RulesEngineQueue, "Submitted", "JOURNEY-1", anchor.AddSeconds(0)),
            Metric(RulesEngineQueue, "RulesEvaluated", "JOURNEY-1", anchor.AddSeconds(15)),
            Metric(RulesEngineQueue, "RulesEvaluated", "OTHER-REF", anchor.AddSeconds(5)));

        var service = CreateService();
        var journey = await service.GetJourneyAsync("JOURNEY-1");

        Assert.Equal(3, journey.Count);
        Assert.Equal("Submitted", journey[0].Stage);
        Assert.Equal("RulesEvaluated", journey[1].Stage);
        Assert.Equal("TicketCreated", journey[2].Stage);
        Assert.All(journey, e => Assert.Equal("JOURNEY-1", e.ReferenceNumber));
    }

    // --- Deploy markers come from RulesConfigVersion rows inside the window ---

    [Fact]
    public async Task GetDeployMarkers_ReturnsRulesConfigVersionsInsideWindow()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        await using (var seed = _fixture.CreateContext())
        {
            seed.RulesConfigVersions.Add(NewVersion(11, anchor.AddMinutes(-10))); // before window
            seed.RulesConfigVersions.Add(NewVersion(12, anchor.AddMinutes(5)));   // inside
            seed.RulesConfigVersions.Add(NewVersion(13, anchor.AddMinutes(50)));  // after window
            await seed.SaveChangesAsync();
        }

        var service = CreateService();
        var markers = await service.GetDeployMarkersAsync(anchor, anchor.AddMinutes(30));

        var marker = Assert.Single(markers);
        Assert.Equal(anchor.AddMinutes(5), marker.CreatedAtUtc);
        Assert.Contains("12", marker.Label);
    }

    // --- An out-of-allow-list granularity is rejected (no raw SQL interpolation path) ---

    [Fact]
    public async Task GetThroughput_RejectsUnknownGranularity()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 1, 9, 10, 0, 0, DateTimeKind.Utc);
        var service = CreateService();

        // A value outside the defined enum must not reach the date_trunc unit map.
        var bogus = (ThroughputGranularity)999;

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.GetThroughputAsync(RulesEngineQueue, bogus, anchor, anchor.AddMinutes(1)));
    }

    // --- An over-wide date range is rejected to prevent abusive aggregation (DoS) ---

    [Fact]
    public async Task GetThroughput_RejectsOverWideRange()
    {
        await ResetMetricsAsync();
        var from = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.GetThroughputAsync(RulesEngineQueue, ThroughputGranularity.Second, from, to));
    }

    // --- The full transactions list pages newest-first in SQL with a total count ---

    [Fact]
    public async Task GetTransactions_PagesNewestFirst_WithTotalCount()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);

        // 25 events, one per minute. Newest is anchor + 24 minutes.
        var events = Enumerable.Range(0, 25)
            .Select(i => Metric(RulesEngineQueue, "RulesEvaluated", $"T-{i:00}", anchor.AddMinutes(i)))
            .ToArray();
        await SeedMetricsAsync(events);

        var service = CreateService();

        var page1 = await service.GetTransactionsAsync(page: 1, pageSize: 20);

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(20, page1.Rows.Count);
        // Newest first: the first row is the last-recorded event (minute 24).
        Assert.Equal("T-24", page1.Rows[0].ReferenceNumber);
        Assert.Equal(anchor.AddMinutes(24), page1.Rows[0].RecordedAtUtc);
        // Strictly descending across the page.
        for (var i = 1; i < page1.Rows.Count; i++)
            Assert.True(page1.Rows[i].RecordedAtUtc <= page1.Rows[i - 1].RecordedAtUtc);

        var page2 = await service.GetTransactionsAsync(page: 2, pageSize: 20);

        // The remaining 5 rows, still newest-first within the page.
        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(5, page2.Rows.Count);
        Assert.Equal("T-04", page2.Rows[0].ReferenceNumber);
        Assert.Equal("T-00", page2.Rows[4].ReferenceNumber);
    }

    // --- A from/to window narrows the transactions list ---

    [Fact]
    public async Task GetTransactions_WindowNarrowsTheList()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "OLD", anchor.AddMinutes(-120)),
            Metric(RulesEngineQueue, "RulesEvaluated", "IN-1", anchor.AddMinutes(5)),
            Metric(RulesEngineQueue, "RulesEvaluated", "IN-2", anchor.AddMinutes(10)));

        var service = CreateService();

        var page = await service.GetTransactionsAsync(
            page: 1, pageSize: 20, fromUtc: anchor, toUtc: anchor.AddMinutes(30));

        Assert.Equal(2, page.TotalCount);
        Assert.DoesNotContain(page.Rows, r => r.ReferenceNumber == "OLD");
    }

    // --- The submissions picker lists distinct references, newest-first, paged in SQL ---

    [Fact]
    public async Task GetSubmissions_GroupsByReference_NewestFirst_WithLatestStage()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        // REF-A: two events; first seen at +0, latest stage TicketCreated at +20 (with a decision).
        // REF-B: one event at +30 (the most recent submission overall).
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "REF-A", anchor.AddSeconds(0)),
            Metric(ZendeskQueue, "TicketCreated", "REF-A", anchor.AddSeconds(20), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "Submitted", "REF-B", anchor.AddSeconds(30)));

        var service = CreateService();
        var page = await service.GetSubmissionsAsync(page: 1, pageSize: 20);

        Assert.Equal(2, page.TotalCount);

        // REF-B is newest by first-seen, so it leads.
        Assert.Equal("REF-B", page.Rows[0].ReferenceNumber);

        var refA = page.Rows.Single(r => r.ReferenceNumber == "REF-A");
        Assert.Equal(anchor.AddSeconds(0), refA.SubmittedAtUtc);     // first-seen
        Assert.Equal("TicketCreated", refA.LatestStage);            // most recent stage
        Assert.Equal("AutoApproved", refA.LatestDecision);
    }

    [Fact]
    public async Task GetSubmissions_PagesDistinctReferences()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

        // 25 distinct references, two events each — paging must be over distinct references, not rows.
        var events = Enumerable.Range(0, 25)
            .SelectMany(i => new[]
            {
                Metric(RulesEngineQueue, "Submitted", $"S-{i:00}", anchor.AddMinutes(i)),
                Metric(ZendeskQueue, "TicketCreated", $"S-{i:00}", anchor.AddMinutes(i).AddSeconds(5)),
            })
            .ToArray();
        await SeedMetricsAsync(events);

        var service = CreateService();

        var page1 = await service.GetSubmissionsAsync(page: 1, pageSize: 20);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(20, page1.Rows.Count);

        var page2 = await service.GetSubmissionsAsync(page: 2, pageSize: 20);
        Assert.Equal(5, page2.Rows.Count);

        var ids1 = page1.Rows.Select(r => r.ReferenceNumber).ToHashSet();
        Assert.DoesNotContain(page2.Rows, r => ids1.Contains(r.ReferenceNumber));
    }

    [Fact]
    public async Task GetSubmissions_WindowNarrowsTheList()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 3, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "OLD", anchor.AddMinutes(-180)),
            Metric(RulesEngineQueue, "Submitted", "NEW", anchor.AddMinutes(5)));

        var service = CreateService();
        var page = await service.GetSubmissionsAsync(
            page: 1, pageSize: 20, fromUtc: anchor, toUtc: anchor.AddMinutes(30));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("NEW", page.Rows.Single().ReferenceNumber);
    }

    // --- A submission's resolved decision comes from its RulesEvaluated event, not the latest stage ---

    [Fact]
    public async Task GetSubmissions_ResolvesDecisionFromRulesEvaluated_NotTheLatestStage()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc);

        // The decision is carried on RulesEvaluated; the later TicketCreated stage has a null decision.
        // A naive "latest event" read would show a blank decision. The resolved decision must be Scrutiny.
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "DEC-1", anchor.AddSeconds(0)),
            Metric(RulesEngineQueue, "RulesEvaluated", "DEC-1", anchor.AddSeconds(10), decision: "Scrutiny"),
            Metric(ZendeskQueue, "TicketCreated", "DEC-1", anchor.AddSeconds(20)));

        var service = CreateService();
        var page = await service.GetSubmissionsAsync(page: 1, pageSize: 20);

        var row = page.Rows.Single(r => r.ReferenceNumber == "DEC-1");
        Assert.Equal("Scrutiny", row.ResolvedDecision);
    }

    [Fact]
    public async Task GetSubmissions_ResolvedDecisionIsNull_WhenNotYetDecided()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 11, 10, 0, 0, DateTimeKind.Utc);

        // Submitted but not yet through the rules engine: no decision anywhere.
        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "PEND-1", anchor.AddSeconds(0)));

        var service = CreateService();
        var page = await service.GetSubmissionsAsync(page: 1, pageSize: 20);

        var row = page.Rows.Single(r => r.ReferenceNumber == "PEND-1");
        Assert.Null(row.ResolvedDecision);
    }

    // --- A transaction row carries its reference's resolved decision (from the RulesEvaluated event) ---

    [Fact]
    public async Task GetTransactions_ResolvedDecisionComesFromRulesEvaluated_ForEveryRowOfTheReference()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "TX-1", anchor.AddSeconds(0)),
            Metric(RulesEngineQueue, "RulesEvaluated", "TX-1", anchor.AddSeconds(10), decision: "AutoRejected"),
            Metric(ZendeskQueue, "TicketCreated", "TX-1", anchor.AddSeconds(20)));

        var service = CreateService();
        var page = await service.GetTransactionsAsync(page: 1, pageSize: 20);

        // Every row of TX-1 (including TicketCreated, whose own decision_status is null) reports the
        // reference's resolved decision so the column is not mysteriously blank.
        foreach (var row in page.Rows.Where(r => r.ReferenceNumber == "TX-1"))
            Assert.Equal("AutoRejected", row.ResolvedDecision);
    }

    // --- The transactions list filters by reference (case-insensitive prefix), keeping pagination ---

    [Fact]
    public async Task GetTransactions_ReferenceFilter_ReturnsOnlyThatReference()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 13, 10, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "RulesEvaluated", "AAA-111", anchor.AddSeconds(0), decision: "Scrutiny"),
            Metric(ZendeskQueue, "TicketCreated", "AAA-111", anchor.AddSeconds(5)),
            Metric(RulesEngineQueue, "RulesEvaluated", "BBB-222", anchor.AddSeconds(10), decision: "AutoApproved"));

        var service = CreateService();

        // Case-insensitive: the lowercase query matches the uppercase reference.
        var page = await service.GetTransactionsAsync(page: 1, pageSize: 20, reference: "aaa-111");

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Rows, r => Assert.Equal("AAA-111", r.ReferenceNumber));
        Assert.DoesNotContain(page.Rows, r => r.ReferenceNumber == "BBB-222");
    }

    [Fact]
    public async Task GetTransactions_ReferenceFilter_KeepsPagination()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);

        // 25 events all for the same reference, plus noise for another reference.
        var events = Enumerable.Range(0, 25)
            .Select(i => Metric(RulesEngineQueue, "RulesEvaluated", "SAME-REF", anchor.AddMinutes(i), decision: "Scrutiny"))
            .Append(Metric(RulesEngineQueue, "RulesEvaluated", "OTHER-REF", anchor.AddMinutes(100), decision: "AutoApproved"))
            .ToArray();
        await SeedMetricsAsync(events);

        var service = CreateService();

        var page1 = await service.GetTransactionsAsync(page: 1, pageSize: 20, reference: "SAME-REF");
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(20, page1.Rows.Count);
        Assert.All(page1.Rows, r => Assert.Equal("SAME-REF", r.ReferenceNumber));

        var page2 = await service.GetTransactionsAsync(page: 2, pageSize: 20, reference: "SAME-REF");
        Assert.Equal(5, page2.Rows.Count);
        Assert.All(page2.Rows, r => Assert.Equal("SAME-REF", r.ReferenceNumber));
    }

    private IMetricsQueryService CreateService() =>
        new MetricsQueryService(_fixture.CreateContext());

    private static MetricEntity Metric(
        string queue,
        string stage,
        string reference,
        DateTime recordedAtUtc,
        string? decision = null,
        double latencyMs = 0) => new()
    {
        QueueName = queue,
        Stage = stage,
        ReferenceNumber = reference,
        MessageId = Guid.NewGuid(),
        DecisionStatus = decision,
        LatencyMs = latencyMs,
        RecordedAtUtc = recordedAtUtc,
    };

    private async Task SeedMetricsAsync(params MetricEntity[] events)
    {
        await using var context = _fixture.CreateContext();
        context.QueueMetricEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private static RulesConfigVersion NewVersion(int versionNumber, DateTime createdAt) => new()
    {
        ConfigType = RulesConfigType.Rules,
        VersionNumber = versionNumber,
        Content = "{}",
        CreatedAt = createdAt,
        CreatedBy = "test",
    };

    private async Task ResetMetricsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE queue_metrics_events RESTART IDENTITY CASCADE;");
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"RulesConfigVersions\";");
    }
}
