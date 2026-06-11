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
