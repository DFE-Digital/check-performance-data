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

// One bucket in the volume-over-time chart. BucketStart is UTC, aligned to the bucket
// granularity picked at query time (see VolumeBucketSize). SearchCount is total events
// in the bucket, UniqueSessionCount is distinct session_ids in the bucket. Both are 0
// for gap-filled empty buckets — the generate_series spine in the reader emits a row
// for every bucket even when no events landed in it.
//
// Reused by the two single-series readers on this interface: unique-sessions-per-bucket
// carries the distinct-session count in SearchCount (UniqueSessionCount stays 0); zero-
// result-count-per-bucket carries the zero-result-event count in SearchCount (again
// UniqueSessionCount stays 0). Keeping one record for all three readers avoids a new
// projection type per tile and lets a single SVG partial render any single-series line.
public sealed record VolumeBucket(
    DateTime BucketStart,
    int SearchCount,
    int UniqueSessionCount);

// One bucket in the latency-percentiles-over-time chart. BucketStart is UTC, aligned to
// the bucket granularity picked at query time. P5Ms / P50Ms / P95Ms are the continuous
// percentiles of latency_ms over the events that landed in that bucket, computed server
// side via percentile_cont. All three fields are 0 for gap-filled empty buckets so the
// three polylines are continuous across the chart X-axis.
public sealed record LatencyBucket(
    DateTime BucketStart,
    int P5Ms,
    int P50Ms,
    int P95Ms);

// The five explicit bucket granularities the chart supports. Callers who do not care can
// keep using the no-bucket overload of GetVolumeOverTimeAsync — the service picks a
// granularity based on the window width. The explicit overload takes this enum so the
// admin can override via a query string.
public enum VolumeBucketSize
{
    FifteenMinutes,
    Hour,
    Day,
    Week,
    Month,
}

// A row in the top-pages drill-in. ResultKey is the page URL (or block key — the same
// column carries either kind of hit). ImpressionCount is how many result rows referenced
// this page in the window. UniqueQueryCount is retained on the record for callers that
// need the distinct-search figure, but the shipped admin surface drops it: the sink
// canonicaliser already ensures each search returns each URL at most once, so the two
// counts are always equal on the surfaces that use this record.
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

// One point on the request-timings scatter chart. OccurredAtUtc is the raw
// search_events.occurred_at_utc; LatencyMs is the raw latency of that event; SessionId
// is the opaque session id (masked in the view to the first 8 characters, full value
// available on hover). Query is the raw query string (empty allowed for a search with
// no term). ResultsTotal is how many hits the search returned. Used by the drill-in
// table underneath the scatter and by the scatter itself to draw one <circle> per row.
public sealed record RequestTimingPoint(
    DateTime OccurredAtUtc,
    int LatencyMs,
    string SessionId,
    string? Query,
    int ResultsTotal);

// One (weekday, hour-of-day) bucket in the heatmap card. Weekday follows ISO 8601 —
// 1 = Monday, 7 = Sunday — because a UK-audience week reads left-to-right starting on
// Monday. Hour is 0..23 UTC. Count is the number of search events whose occurred_at_utc
// falls in that (weekday, hour) tuple over the selected window. Zero-filled — the read
// returns every (weekday, hour) tuple in the 7 × 24 = 168-cell grid even if that cell
// has no events in the window so the SVG heatmap has no visual gaps.
public sealed record WeekdayHourBucket(
    int Weekday,
    int Hour,
    int Count);

// Session-level rollup for the "Zero-result outcomes" funnel card. Answers the question
// "of sessions that hit a zero-result search in this window, what did they do next?".
// Every zero-result session is bucketed into exactly one outcome; the sums equal
// TotalZeroResultSessions. Tie-break rule when a session both refined AND submitted
// feedback: refined wins (a query change is a stronger signal of "the user kept looking"
// than a message — the message could have been submitted about a different search).
// TotalSessionsInWindow gives the subtitle its denominator context (X of Y sessions
// had at least one zero-result search).
public sealed record ZeroResultOutcomeSummary(
    int TotalZeroResultSessions,
    int TotalSessionsInWindow,
    int RefinedCount,
    int FeedbackCount,
    int SilentCount);

// Prior-window summary alongside the current-window one — drives the week-over-week
// anomaly chips beneath each of the four stat tiles. Every field mirrors a field on
// SearchAnalyticsSummary so the view can compute (current - prior) / prior in one
// place. Available is false when the prior window would fall outside the sink's 90-day
// retention (a 45-day+ current window's prior would start > 90 days ago) — the view
// hides all four chips and shows an "insufficient prior data" hint when this flag is
// false.
public sealed record SearchAnalyticsSummaryDeltas(
    int PriorTotalCount,
    int PriorUniqueSessions,
    double PriorZeroResultRatePercent,
    int PriorP95LatencyMs,
    bool Available);

// Headline recovery statistics for zero-result-having sessions in the current window.
// Contextualises the "N% of queries returned zero results" summary tile — most of those
// users refine and get answers, so the raw zero-result rate over-reads as "N% of users
// failed" when actually only a small subset genuinely couldn't find what they wanted.
public sealed record ZeroResultRecoveryStats(
    int SessionsWithAnyZeroResult,
    int RecoveredCount,        // eventually got a >0-result event after any zero-result
    int AbandonedCount,        // never got a >0-result event AND did not send feedback
    int SentFeedbackCount);    // sent a feedback message (may or may not have recovered)

// One step in a session's refinement chain — the raw event, ordered oldest-first by
// occurred_at_utc.
public sealed record RefinementStep(
    DateTime OccurredAtUtc,
    string? Query,
    int ResultsTotal);

// A single session's complete refinement journey. Only sessions with at least one
// zero-result event during the window are included. The steps list captures every
// search this session ran in the window (chronological), so a reader can see exactly
// what the user tried and whether they eventually recovered.
public sealed record ZeroResultJourney(
    string SessionId,
    IReadOnlyList<RefinementStep> Steps,
    bool EventuallyRecovered,
    bool SentFeedback);
