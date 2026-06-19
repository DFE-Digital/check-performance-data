namespace DfE.CheckPerformanceData.Application.Observability;

// One queue metric event as shown on the full transactions list: the timestamp, the request
// reference, the stage and queue it passed through, the decision it carried (if any) and the
// recorded latency. This is the same append-only event the journey/board read from, projected
// for a flat newest-first table.
//
// DecisionStatus is the decision on THIS event (only the RulesEvaluated stage carries one;
// later stages such as TicketCreated are null). ResolvedDecision is the reference's decision —
// the decision_status from that reference's RulesEvaluated event — surfaced on every row of the
// reference so the column is not mysteriously blank on the non-RulesEvaluated rows.
public sealed record TransactionRow(
    DateTime RecordedAtUtc,
    string ReferenceNumber,
    string Stage,
    string QueueName,
    string? DecisionStatus,
    double LatencyMs,
    string? ResolvedDecision = null);

// A single page of transactions plus the total row count, so the view can render a pager without
// loading every row. The rows are paged in SQL (LIMIT/OFFSET) and the total comes from a COUNT —
// the full table is never materialised into memory.
public sealed record TransactionsPage(
    IReadOnlyList<TransactionRow> Rows,
    int TotalCount,
    int Page,
    int PageSize);
