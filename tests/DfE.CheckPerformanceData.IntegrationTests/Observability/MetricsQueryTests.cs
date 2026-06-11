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
[Trait("Category", "W0")]
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
