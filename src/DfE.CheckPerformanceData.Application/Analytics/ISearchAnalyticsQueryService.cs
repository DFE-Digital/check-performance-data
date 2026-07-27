namespace DfE.CheckPerformanceData.Application.Analytics;

// Read-side over search_events. This is the single query surface the search-analytics
// admin dashboard reads through: aggregate tiles, the top-N tables, an empty-state
// row-count guard, and a helper the feedback form uses to pre-fill "what did you
// actually get?" from the last search in the current session. Aggregation is
// database-side; every window bound is a server-owned DateTime and every SQL parameter
// binds via Npgsql so the read path has no injection surface.
//
// Later plans extend this surface (a volume-over-time bucketed read, a top-pages
// read grouped by search_event_results.result_key, and a session-scoped history
// projection) — those additions are additive; the four members below carry the
// landing dashboard on their own.
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
}
