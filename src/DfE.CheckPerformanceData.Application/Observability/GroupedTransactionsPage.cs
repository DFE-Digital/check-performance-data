namespace DfE.CheckPerformanceData.Application.Observability;

// One message's transactions collapsed onto a single row, for the transactions page's
// "group by message" view. Built by aggregating that reference's recorded events: the
// Submitted / RulesEvaluated / TicketCreated timestamps, the decision and rules-engine
// latency from the RulesEvaluated event, and whether it was dead-lettered. The view derives
// the per-queue waits from the timestamps, so the grouped row mirrors the dashboard matrix.
public sealed record GroupedTransactionRow(
    string ReferenceNumber,
    DateTime? SubmittedAtUtc,
    DateTime? RulesEvaluatedAtUtc,
    DateTime? TicketCreatedAtUtc,
    string? Decision,
    double? RulesLatencyMs,
    bool DeadLettered,
    DateTime LastActivityUtc);

// A single page of grouped (per-message) transactions plus the total distinct-reference count,
// so the view can render a pager. The grouping, ordering and paging all happen in SQL; the full
// event history is never materialised.
public sealed record GroupedTransactionsPage(
    IReadOnlyList<GroupedTransactionRow> Rows,
    int TotalCount,
    int Page,
    int PageSize);
