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

    // Per-bucket counts per decision status across [from, to), gap-filled to zero for every
    // status present in the window, so the over-time chart's series share one unbroken time
    // axis. An empty result means no decided events in the window. Throws ArgumentException
    // for an unknown granularity or an over-wide range, as for throughput.
    Task<IReadOnlyList<DecisionMixBucket>> GetDecisionMixOverTimeAsync(
        ThroughputGranularity granularity,
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

    // A newest-first page of every recorded queue metric event (the full transactions list).
    // Paged in SQL with LIMIT/OFFSET and a separate COUNT, so the whole table is never loaded
    // into memory to be paged in process. An optional [from, to) window narrows the list; when
    // supplied it is bounded by the same abusive-aggregation guard as the chart reads. An optional
    // reference filters to one request (case-insensitive prefix on reference_number), in SQL,
    // sharing the predicate between the COUNT and the page so pagination stays correct. Each row
    // also carries its reference's ResolvedDecision (from that reference's RulesEvaluated event)
    // so the decision column is populated even on the rows whose own stage carries no decision.
    // An optional set of stage values (Submitted / RulesEvaluated / TicketCreated / DeadLettered)
    // narrows the list to just those stages, so a tester can see e.g. only the submission rows. The
    // sort key is validated against TransactionSort's allow-list before reaching the ORDER BY, with
    // a direction; an unknown key falls back to newest-first.
    Task<TransactionsPage> GetTransactionsAsync(
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? reference = null,
        IReadOnlyList<string>? stages = null,
        string? sortKey = null,
        bool descending = true,
        CancellationToken cancellationToken = default);

    // A newest-first page of transactions GROUPED by message: one row per distinct reference,
    // aggregating its Submitted / RulesEvaluated / TicketCreated timestamps, decision, rules-engine
    // latency and dead-letter flag, so the transactions page can show each message on one line
    // across the pipeline stages. The grouping, ordering (by last activity, newest first) and paging
    // (LIMIT/OFFSET plus a COUNT of distinct references) all happen in SQL. The same optional
    // [from, to) window and reference prefix filter as the flat list apply, bounded by the same
    // abusive-aggregation guard.
    Task<GroupedTransactionsPage> GetGroupedTransactionsAsync(
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? reference = null,
        CancellationToken cancellationToken = default);

    // A newest-first page of distinct request references that entered the pipeline, each with its
    // first-seen time and its most recent stage/decision — the picker for the interactive replay.
    // The distinct grouping, ordering and paging all happen in SQL (a grouped query paged with
    // LIMIT/OFFSET plus a COUNT of the distinct references); the full event history is never
    // materialised. An optional [from, to) window narrows the list, bounded by the same
    // abusive-aggregation guard as the chart reads.
    Task<SubmissionsPage> GetSubmissionsAsync(
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    // Rules-config deploy markers whose CreatedAt falls inside [from, to].
    Task<IReadOnlyList<DeployMarker>> GetDeployMarkersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // The current live snapshot for the board / dashboard stream.
    Task<ObservabilitySnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default);

    // The aggregate-only projection for the anonymised share link and wallboard. Selects ONLY
    // aggregate columns (counts, decision-mix totals, throughput buckets, dwell) — never a
    // reference number, payload or any pupil-bearing column — so the share surfaces carry no pupil
    // data by construction. The overall health band is left to the caller to compute from the
    // returned depths so this read stays a pure aggregate query.
    Task<AggregateShareSnapshot> GetAggregateShareAsync(CancellationToken cancellationToken = default);
}
