namespace DfE.CheckPerformanceData.Application.Observability;

// One submission as shown on the replay picker: a distinct request reference that entered the
// pipeline, the time it was first seen (its earliest recorded event), and the stage/decision of
// its most recent recorded event. Selecting these and pressing Play follows them across the
// pipeline stages.
//
// LatestDecision is the decision on the most recent event (often null, because the latest stage —
// TicketCreated — carries no decision). ResolvedDecision is the reference's actual decision, taken
// from its RulesEvaluated event (the only stage that carries one), so the decision column shows the
// real outcome rather than the latest stage's blank.
public sealed record SubmissionRow(
    string ReferenceNumber,
    DateTime SubmittedAtUtc,
    string LatestStage,
    string? LatestDecision,
    string? ResolvedDecision = null);

// A page of submissions plus the total distinct-reference count, so the picker can render a pager
// without loading every reference. The distinct grouping, ordering and paging are all done in SQL
// (LIMIT/OFFSET over a grouped query + a COUNT of the distinct references).
public sealed record SubmissionsPage(
    IReadOnlyList<SubmissionRow> Rows,
    int TotalCount,
    int Page,
    int PageSize);
