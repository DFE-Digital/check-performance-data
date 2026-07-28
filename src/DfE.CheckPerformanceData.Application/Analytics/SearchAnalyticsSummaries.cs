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

// One bucket in the volume-over-time chart. BucketStart is UTC, aligned to hour or day
// boundaries depending on the window granularity. SearchCount is total events in the
// bucket, UniqueSessionCount is distinct session_ids in the bucket. Both are 0 for
// gap-filled empty buckets — the generate_series spine in the reader emits a row for
// every bucket even when no events landed in it.
public sealed record VolumeBucket(
    DateTime BucketStart,
    int SearchCount,
    int UniqueSessionCount);

// A row in the top-pages drill-in. ResultKey is the page URL (or block key — the same
// column carries either kind of hit). ImpressionCount is how many result rows referenced
// this page in the window; UniqueQueryCount is how many distinct parent search events
// returned it (i.e. distinct searches whose results included this page).
public sealed record TopPageRow(
    string ResultKey,
    int ImpressionCount,
    int UniqueQueryCount);

// The last search a session ran, used by the feedback form so the user can see (and support
// can later see) exactly what the system returned for their query. Nullable return from the
// query service when the session has never searched.
//
// Hits carries the individual result rows the search produced (page + block impressions) in
// their rendered order so the feedback form can display "here are the pages we sent you to"
// rather than just a count — a count is not actionable for the user or the support team.
public sealed record SearchEventForPrefill(
    string? QueryRaw,
    string? QueryNormalised,
    int ResultsTotal,
    DateTime OccurredAtUtc,
    IReadOnlyList<SearchHitPrefill> Hits);

// One hit in the "your last search returned" list on the feedback form. ResultKind is
// "page" or "block" — matching the sink invariant. ResultKey is the canonical URL for
// pages or the block key for content blocks; the view renders it verbatim.
public sealed record SearchHitPrefill(
    int Position,
    string ResultKind,
    string ResultKey);
