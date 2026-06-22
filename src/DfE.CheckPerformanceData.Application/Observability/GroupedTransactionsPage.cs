namespace DfE.CheckPerformanceData.Application.Observability;

// One message's transactions collapsed onto a single row, for the transactions page's
// "group by message" view. Built by aggregating that reference's recorded events: the
// Submitted / RulesEvaluated / TicketCreated timestamps, the decision and rules-engine
// latency from the RulesEvaluated event, and whether it was dead-lettered. The view derives
// the per-queue waits from the timestamps, so the grouped row mirrors the dashboard matrix.
//
// RulesStartedAtUtc / TicketStartedAtUtc are when the consumer DEQUEUED the message at that
// stage (began processing), so a real queue wait (started − previous recorded) and processing
// time (recorded − started) can be shown separately instead of one combined span. Null for rows
// recorded before the started time was captured.
public sealed record GroupedTransactionRow(
    string ReferenceNumber,
    DateTime? SubmittedAtUtc,
    DateTime? RulesEvaluatedAtUtc,
    DateTime? TicketCreatedAtUtc,
    string? Decision,
    double? RulesLatencyMs,
    bool DeadLettered,
    DateTime LastActivityUtc,
    DateTime? RulesStartedAtUtc = null,
    DateTime? TicketStartedAtUtc = null);

// A single page of grouped (per-message) transactions plus the total distinct-reference count,
// so the view can render a pager. The grouping, ordering and paging all happen in SQL; the full
// event history is never materialised.
public sealed record GroupedTransactionsPage(
    IReadOnlyList<GroupedTransactionRow> Rows,
    int TotalCount,
    int Page,
    int PageSize);
