using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The view model behind the anonymised share link and the wallboard. It is a SEPARATE type from
// the authenticated DashboardViewModel and carries ZERO pupil-bearing properties BY CONSTRUCTION —
// only aggregate counts, decision-mix totals, throughput buckets, per-stage dwell, the processed
// total, a typical end-to-end duration and the overall health band. There is no reference number,
// no payload, no journey and no per-message detail anywhere in its graph, so these surfaces cannot
// leak pupil data even if a future change is careless: the data simply is not here. A reflection
// test enforces this property-shape contract.
public sealed class AggregateShareViewModel
{
    public required HealthState OverallHealth { get; init; }

    public int ProcessedToday { get; init; }
    public TimeSpan TypicalEndToEnd { get; init; }

    public IReadOnlyList<QueueDepthSnapshot> Depths { get; init; } = Array.Empty<QueueDepthSnapshot>();
    public IReadOnlyList<ThroughputBucket> Throughput { get; init; } = Array.Empty<ThroughputBucket>();
    public IReadOnlyList<DecisionMixEntry> DecisionMix { get; init; } = Array.Empty<DecisionMixEntry>();
    public IReadOnlyList<StageDwell> Dwell { get; init; } = Array.Empty<StageDwell>();

    public DateTime CapturedAtUtc { get; init; }

    public static AggregateShareViewModel From(AggregateShareSnapshot snapshot) => new()
    {
        OverallHealth = snapshot.OverallHealth,
        ProcessedToday = snapshot.ProcessedToday,
        TypicalEndToEnd = snapshot.TypicalEndToEnd,
        Depths = snapshot.Depths,
        Throughput = snapshot.Throughput,
        DecisionMix = snapshot.DecisionMix,
        Dwell = snapshot.Dwell,
        CapturedAtUtc = snapshot.CapturedAtUtc,
    };
}
