namespace DfE.CheckPerformanceData.Application.Analytics;

// Read-side projections over search_events. Records are aggregate shapes only — no
// row-level identity leaks through: the sink stores an opaque session id and nothing
// else, so every dashboard-tier projection here can be shown to any admin without
// PII masking machinery.

// The 4-tile summary carried by the landing dashboard. TotalCount is 0 when the
// window is empty; ZeroResultRatePercent is 0..100.0 (rendered as "42.3%" in the view);
// P95LatencyMs is the server-side percentile_cont(0.95) over the rows in the window,
// truncated to a whole millisecond because the tile does not need sub-ms precision.
public sealed record SearchAnalyticsSummary(
    int TotalCount,
    int UniqueSessions,
    double ZeroResultRatePercent,
    int P95LatencyMs);

// A row in the top-N tables. QueryNormalised is the aggregation key (never null in the
// projection — the read filters rows whose normalised value is null before grouping,
// because a null aggregation key is a data-shape problem, not a bucket). ZeroResultCount
// is how many of the Count rows for that query returned nothing — the "top zero-result"
// table filters to rows where Count == ZeroResultCount.
public sealed record TopQueryRow(
    string QueryNormalised,
    int Count,
    int ZeroResultCount);

// The last search a session ran, used by the feedback form's "what did you actually get?"
// pre-fill. Nullable return from the query service when the session has never searched.
public sealed record SearchEventForPrefill(
    string? QueryRaw,
    string? QueryNormalised,
    int ResultsTotal,
    DateTime OccurredAtUtc);
