namespace DfE.CheckPerformanceData.Application.Observability;

// A single point-in-time bucket of throughput for one queue. Empty buckets in a range are
// gap-filled to a zero count so a chart never shows a broken time axis.
public sealed record ThroughputBucket(DateTime BucketStartUtc, int Count);

// Average dwell (latency) recorded for messages at a given pipeline stage in a time window.
public sealed record StageDwell(string Stage, double AverageLatencyMs);

// Count of recorded events carrying a given decision status in a time window.
public sealed record DecisionMixEntry(string DecisionStatus, int Count);

// One stage event in a single message's journey through the pipeline.
public sealed record JourneyEvent(
    string Stage,
    string ReferenceNumber,
    string QueueName,
    string? DecisionStatus,
    double LatencyMs,
    DateTime RecordedAtUtc);

// A rules-config deploy marker drawn as a vertical annotation on the throughput / decision
// charts. Sourced from RulesConfigVersion rows within the queried window.
public sealed record DeployMarker(DateTime CreatedAtUtc, string Label);

// The live payload streamed to the board / dashboard: current per-queue depths, the most
// recent stage transitions, and the running decision-mix totals.
public sealed record ObservabilitySnapshot(
    IReadOnlyList<QueueDepthSnapshot> Depths,
    IReadOnlyList<JourneyEvent> RecentTransitions,
    IReadOnlyList<DecisionMixEntry> DecisionMix,
    DateTime CapturedAtUtc);

public sealed record QueueDepthSnapshot(string QueueName, int Depth, TimeSpan? OldestMessageAge);

// The aggregate-only projection behind the anonymised share link and the wallboard. It carries
// ONLY counts, rates, decision-mix totals, throughput buckets, per-stage dwell, the processed
// total, a typical end-to-end duration and the overall health band — no reference number, no
// payload, no journey, nothing per-pupil. The share view model is built from this so the share
// surfaces leak nothing by construction. Note: HealthState carries the OverallHealth band only,
// itself derived from queue depths/ages/DLQ rate — never any pupil field.
public sealed record AggregateShareSnapshot(
    IReadOnlyList<QueueDepthSnapshot> Depths,
    IReadOnlyList<DecisionMixEntry> DecisionMix,
    IReadOnlyList<ThroughputBucket> Throughput,
    IReadOnlyList<StageDwell> Dwell,
    int ProcessedToday,
    TimeSpan TypicalEndToEnd,
    HealthState OverallHealth,
    DateTime CapturedAtUtc);
