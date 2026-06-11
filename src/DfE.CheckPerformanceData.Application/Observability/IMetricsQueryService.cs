namespace DfE.CheckPerformanceData.Application.Observability;

// Read-side over queue_metrics_events. This is the single query surface every downstream
// observability consumer reads through (throughput charts, dwell, decision-mix, the
// per-message journey timeline, board replay and the live snapshot). Time-bucket
// aggregation is done database-side; the granularity is chosen from a server-side
// allow-list and every value is parameterised, so there is no SQL-injection path.
public interface IMetricsQueryService
{
    // Per-bucket throughput for one queue across [from, to), with empty buckets gap-filled
    // to a zero count. Throws ArgumentException for an unknown granularity or an over-wide
    // range (abusive-aggregation guard).
    Task<IReadOnlyList<ThroughputBucket>> GetThroughputAsync(
        string queueName,
        ThroughputGranularity granularity,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // Average dwell (latency) per stage across [from, to).
    Task<IReadOnlyList<StageDwell>> GetDwellByStageAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // Count of events per decision status across [from, to].
    Task<IReadOnlyList<DecisionMixEntry>> GetDecisionMixAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // The ordered stage events for one reference number (the journey timeline source).
    Task<IReadOnlyList<JourneyEvent>> GetJourneyAsync(
        string referenceNumber,
        CancellationToken cancellationToken = default);

    // The recorded stage transitions across [from, to), ordered chronologically, for the
    // always-on board replay. The same recorded events the board animates live are replayed
    // through the one animation engine on a scrubber clock. The range is bounded against an
    // abusive aggregation request, as for throughput.
    Task<IReadOnlyList<JourneyEvent>> GetReplayWindowAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // Rules-config deploy markers whose CreatedAt falls inside [from, to].
    Task<IReadOnlyList<DeployMarker>> GetDeployMarkersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // The current live snapshot for the board / dashboard stream.
    Task<ObservabilitySnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default);
}
