namespace DfE.CheckPerformanceData.Application.Observability;

// A single point-in-time bucket of throughput for one queue. Empty buckets in a range are
// gap-filled to a zero count so a chart never shows a broken time axis.
public sealed record ThroughputBucket(DateTime BucketStartUtc, int Count);

// Average dwell (latency) recorded for messages at a given pipeline stage in a time window.
public sealed record StageDwell(string Stage, double AverageLatencyMs);

// The average time, in milliseconds, a message spends at each step of the pipeline across a time
// window: waiting in the rules-engine queue (Submitted → RulesEvaluated), being evaluated by the
// rules engine (the RulesEvaluated latency), waiting in the Zendesk queue (RulesEvaluated →
// TicketCreated) and creating the Zendesk ticket (the TicketCreated latency). Each is null when no
// message completed that step in the window. Computed per-message then averaged, database-side.
public sealed record StageAverages(
    double? RulesQueueMs,
    double? RulesEngineMs,
    double? ZendeskQueueMs,
    double? TicketMs);

// Count of recorded events carrying a given decision status in a time window.
public sealed record DecisionMixEntry(string DecisionStatus, int Count);

// A measurement of one load-test batch: how many of the driven references have reached a terminal
// stage (a ticket or a dead-letter), and the per-step averages over that batch. Scoped to an exact
// set of references so a load level measures only its own messages, not other traffic.
public sealed record LoadSample(int Completed, StageAverages Averages);

// One cell of the decision-mix-over-time series: how many events carried a given decision
// status within one time bucket. Buckets with no events for a status present in the window
// are gap-filled to zero so a chart never shows a broken time axis.
public sealed record DecisionMixBucket(DateTime BucketStartUtc, string DecisionStatus, int Count);

// One stage event in a single message's journey through the pipeline. StartedAtUtc is when the
// consumer began processing this stage (null for Submitted / events recorded before this was
// captured), so the board can show the real queue wait and processing split rather than one span.
public sealed record JourneyEvent(
    string Stage,
    string ReferenceNumber,
    string QueueName,
    string? DecisionStatus,
    double LatencyMs,
    DateTime RecordedAtUtc,
    DateTime? StartedAtUtc = null);

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
