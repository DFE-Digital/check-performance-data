namespace DfE.CheckPerformanceData.Application.Analytics;

// Read-side projections over search_messages. Row shapes for the admin inbox list, its
// per-message detail view, and the session-scoped purge return value. Kept alongside
// ISearchMessageService because the interface's read methods return these types
// exclusively — the projection shape is a contract of the service, not of a caller.

// An inbox-row projection. FirstLinePreview is the first ~80 characters of the message
// body, newlines collapsed to spaces, HTML-encoded downstream by Razor — never render
// the raw message text without encoding. HasEmail is a bool badge, not the email itself,
// so the inbox listing never displays a stored address.
public sealed record SearchMessageSummary(
    long Id,
    DateTime SubmittedAtUtc,
    string SessionId,
    bool HasEmail,
    bool IsRead,
    string FirstLinePreview);

// Full detail for the message-detail view. WhatLookingFor and WhatGot are the free-text
// fields the user typed; the view HTML-encodes them at render time. Email is only ever
// populated when the user opted in on the form — a NULL email means "hide my email was
// ticked" (dropped before persist by the feedback controller).
public sealed record SearchMessageDetail(
    long Id,
    DateTime SubmittedAtUtc,
    string SessionId,
    string WhatLookingFor,
    string? WhatGot,
    string? Email,
    bool IsRead,
    string? ReadByAdminSub,
    DateTime? ReadAtUtc);

// Sort dimensions the inbox list supports. SubmittedAt is the default; IsRead flips the
// list to unread-first when the admin wants to catch up on new items.
public enum SearchMessageSortField
{
    SubmittedAt,
    IsRead,
}

// (page rows, total rows) pair returned by the paged inbox reader. TotalCount is the
// filtered count so the pagination widget renders against the same predicate the row
// query does — the widget page counts never drift from the pageable set.
public sealed record PagedResult<T>(IReadOnlyList<T> Rows, int TotalCount);

// Per-table counts returned by the session-scoped purge. Currently only messages,
// because the controller deletes events + results directly (via ExecuteDeleteAsync
// on the parent SearchEvents set, with CASCADE cleaning the child rows) and combines
// its own counts with this one into the AuditEntry payload.
public sealed record SessionPurgeCounts(int MessagesDeleted);

// One row in the session-scoped drill-in table. Shape mirrors the underlying search_events
// columns — the admin sees exactly what the user submitted, when, at what scope, with
// what result counts and latency. Ordered by OccurredAtUtc DESC when returned from
// ISearchAnalyticsQueryService.GetSessionHistoryAsync.
public sealed record SessionHistoryRow(
    DateTime OccurredAtUtc,
    string? QueryRaw,
    string? QueryNormalised,
    string? Scope,
    int ResultsPages,
    int ResultsBlocks,
    int ResultsTotal,
    int LatencyMs);
