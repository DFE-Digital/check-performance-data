namespace DfE.CheckPerformanceData.Application.Analytics;

// Read-side over search_events (and search_event_results for the top-pages drill-in). This
// is the single query surface the search-analytics admin dashboard reads through: aggregate
// tiles, top-N tables, a volume-over-time chart, the three paged drill-in tables, an empty-
// state row-count guard, and a helper the feedback form uses to pre-fill "what did you
// actually get?" from the last search in the current session. Aggregation is database-side;
// every window bound is a server-owned DateTime and every SQL parameter binds via Npgsql so
// the read path has no injection surface.
public interface ISearchAnalyticsQueryService
{
    // The 4-tile summary for the landing dashboard: total searches, distinct session
    // count, zero-result rate as a 0..100 percentage, and the p95 latency in ms.
    Task<SearchAnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // The top-N normalised queries in the window, ordered by count DESC. Rows whose
    // query_normalised is NULL are excluded server-side — a null aggregation key would
    // collapse every unlabelled row into one indistinguishable bucket, which is a bug
    // not a feature.
    Task<IReadOnlyList<TopQueryRow>> GetTopQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken = default);

    // The top-N normalised queries in the window that returned zero results. Same
    // shape as GetTopQueriesAsync, filtered to zero_results = true. The "write this
    // content next" list.
    Task<IReadOnlyList<TopQueryRow>> GetTopZeroResultQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken = default);

    // The empty-state guard used by the landing view. Cheap COUNT(*) — the tiles + the
    // top-N reads run only when this returns >= 20; below that the view renders a
    // single empty-state panel instead.
    Task<int> GetRowCountAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // The newest search event for the given session id, or null if the session has
    // never searched. Used by the feedback form to pre-fill "what did you actually
    // get?" so the user does not have to retype the query. Not window-bounded — the
    // support flow needs the last search in the session's lifetime.
    Task<SearchEventForPrefill?> GetLatestSearchForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    // The newest search event for the given session id whose occurred_at_utc is at or
    // before the supplied bound, or null if no such event exists. Used by the admin
    // message-detail page so the reviewer sees the search snapshot the user was
    // looking at when they submitted the note — a later search would be misleading.
    Task<SearchEventForPrefill?> GetLatestSearchForSessionAtOrBeforeAsync(
        string sessionId,
        DateTime atOrBeforeUtc,
        CancellationToken cancellationToken = default);

    // Bucketed search + unique-session count over the window, used by the volume-over-time
    // chart. Auto-picks a bucket granularity based on window width: <= 48h → hour,
    // <= 30d → day, wider → week. Every bucket in the range is returned — empty buckets
    // are zero-filled via a generate_series LEFT JOIN so the chart's X axis is continuous
    // even for a window with no data.
    Task<IReadOnlyList<VolumeBucket>> GetVolumeOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    // Explicit-bucket-size overload used when the admin picked a granularity via the URL.
    // The reader honours the caller's choice verbatim — no width-based fallback. Same
    // gap-fill and shape as the auto-picking overload.
    Task<IReadOnlyList<VolumeBucket>> GetVolumeOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default);

    // Distinct-session count per bucket over the window. Same generate_series spine +
    // gap-fill as the volume reader; each returned VolumeBucket carries the distinct
    // session_id count in SearchCount (UniqueSessionCount stays 0). Feeds the interactive
    // "Unique users" chart on the search-analytics dashboard.
    Task<IReadOnlyList<VolumeBucket>> GetUniqueSessionsOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default);

    // Zero-result event count per bucket over the window. Same spine as the volume reader
    // filtered to zero_results = true; SearchCount carries the count of zero-result events
    // per bucket. Feeds the interactive "Zero-result rate" chart — the tile shows the
    // rate over the whole window, the chart shows when the zero-result queries actually
    // occurred (per Lance's ask: "the number of result counts in the graph below and what
    // time they occurred").
    Task<IReadOnlyList<VolumeBucket>> GetZeroResultCountOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default);

    // Continuous latency percentiles (p5 / p50 / p95) per bucket over the window. Same
    // spine + gap-fill; empty buckets carry 0 for all three percentiles so the polylines
    // stay continuous across the chart. Server computes percentile_cont — no client-side
    // percentile math. Feeds the interactive "P95 latency" chart with three lines.
    Task<IReadOnlyList<LatencyBucket>> GetLatencyPercentilesOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default);

    // Paged variant of GetTopQueriesAsync: returns one page of the ordered top queries
    // together with the total distinct-query count so the view can render pagination.
    // The COUNT and the page share the identical WHERE / GROUP BY so the total matches
    // the pageable set exactly.
    Task<(IReadOnlyList<TopQueryRow> Rows, int TotalCount)> GetPagedTopQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Paged variant of GetTopZeroResultQueriesAsync — same shape as GetPagedTopQueriesAsync,
    // filtered to zero_results = true.
    Task<(IReadOnlyList<TopQueryRow> Rows, int TotalCount)> GetPagedTopZeroResultQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Top pages by impression count in the window. Reads search_event_results joined to
    // search_events, groups by result_key (URL), returns pages ordered by impressions DESC.
    // UniqueQueryCount is the number of distinct parent search events that returned the page
    // — the "how many different searches surfaced this page" figure.
    Task<(IReadOnlyList<TopPageRow> Rows, int TotalCount)> GetTopPagesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Every search_event for the session, newest first. Not window-bounded: the support
    // flow needs the entire session lifetime so an admin looking at a message can see
    // every search that session ran. Callers filter empty lists into a 404 rather than
    // rendering an empty drill-in — an empty result means "unknown session".
    Task<IReadOnlyList<SessionHistoryRow>> GetSessionHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
