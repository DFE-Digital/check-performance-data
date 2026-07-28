using System.Data;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Reads search_events (and search_event_results for the top-pages drill-in) for the
// search-analytics admin dashboard. Aggregation is database-side: a single window query returns
// total / distinct-sessions / zero-count / p95 for the tiles; grouped queries return the top-N
// normalised queries and the top-N zero-result queries; a bucketed generate_series spine gap-
// fills the volume-over-time chart to zero for empty buckets; the three drill-in readers return
// (Rows, TotalCount) so the view can render pagination in one call; a cheap COUNT gates the
// empty-state; and a LINQ read returns the newest event for a given session id (feedback-form
// pre-fill). Every value crosses as an Npgsql parameter, so the raw SQL has no injection path.
//
// Interface lives in Application; the implementation lives here because it reads the
// shared DbContext directly via raw Npgsql (same layering used for IMetricsSink /
// DbMetricsSink and MetricsQueryService). The Application project cannot reference
// Persistence, so the database-touching read side belongs on this side of the boundary.
public sealed class SearchAnalyticsQueryService : ISearchAnalyticsQueryService
{
    // The chart granularity flips from hour to day at this window width. Chosen so a 2-day
    // window still bucketed by hour renders 48 columns (readable) but anything wider bucketed
    // by hour would exceed the SVG's practical column count. Applied to (toUtc - fromUtc) —
    // any window strictly greater than 48 hours reads day-granularity buckets.
    private static readonly TimeSpan HourBucketThreshold = TimeSpan.FromHours(48);

    private readonly IPortalDbContext _dbContext;

    public SearchAnalyticsQueryService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SearchAnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        // One query returns all four tile numbers in a single scan of the window. The
        // FILTER clause on zero_results computes the zero-result count without a second
        // pass; percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms) is Postgres's
        // continuous percentile aggregate — interpolates between the two rows surrounding
        // the 95th percentile, so the tile stays smooth as the window slides.
        const string sql = @"
SELECT
    COUNT(*)::int AS total_count,
    COUNT(DISTINCT session_id)::int AS unique_sessions,
    COUNT(*) FILTER (WHERE zero_results)::int AS zero_count,
    COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms), 0) AS p95_latency
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to;";

        var totalCount = 0;
        var uniqueSessions = 0;
        var zeroCount = 0;
        var p95 = 0d;
        var found = false;

        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            totalCount = reader.GetInt32(0);
            uniqueSessions = reader.GetInt32(1);
            zeroCount = reader.GetInt32(2);
            p95 = reader.IsDBNull(3) ? 0d : Convert.ToDouble(reader.GetValue(3));
            found = true;
        });

        if (!found)
            return new SearchAnalyticsSummary(0, 0, 0d, 0);

        var zeroRate = totalCount == 0 ? 0d : 100.0 * zeroCount / totalCount;
        return new SearchAnalyticsSummary(
            totalCount,
            uniqueSessions,
            zeroRate,
            (int)Math.Round(p95, MidpointRounding.AwayFromZero));
    }

    public async Task<IReadOnlyList<TopQueryRow>> GetTopQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    query_normalised,
    COUNT(*)::int AS c,
    SUM(CASE WHEN zero_results THEN 1 ELSE 0 END)::int AS z
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to AND query_normalised IS NOT NULL
GROUP BY query_normalised
ORDER BY c DESC, query_normalised ASC
LIMIT @limit;";

        var rows = new List<TopQueryRow>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });
        }, reader =>
        {
            rows.Add(new TopQueryRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        });

        return rows;
    }

    public async Task<IReadOnlyList<TopQueryRow>> GetTopZeroResultQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Filter to zero_results = true so the aggregate reflects only queries that
        // returned nothing. ZeroResultCount equals Count for every row in this projection
        // by construction — the view uses both fields so the top-queries and top-zero
        // tables share one row shape.
        const string sql = @"
SELECT
    query_normalised,
    COUNT(*)::int AS c,
    COUNT(*)::int AS z
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
  AND query_normalised IS NOT NULL
  AND zero_results = true
GROUP BY query_normalised
ORDER BY c DESC, query_normalised ASC
LIMIT @limit;";

        var rows = new List<TopQueryRow>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });
        }, reader =>
        {
            rows.Add(new TopQueryRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        });

        return rows;
    }

    public async Task<int> GetRowCountAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SearchEvents
            .Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)
            .CountAsync(cancellationToken);
    }

    public Task<SearchEventForPrefill?> GetLatestSearchForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ReadLatestSearchForSessionAsync(sessionId, atOrBeforeUtc: null, cancellationToken);

    public Task<SearchEventForPrefill?> GetLatestSearchForSessionAtOrBeforeAsync(
        string sessionId,
        DateTime atOrBeforeUtc,
        CancellationToken cancellationToken = default) =>
        ReadLatestSearchForSessionAsync(sessionId, atOrBeforeUtc, cancellationToken);

    // Shared read backing both public overloads. The window bound is optional: null means "no
    // upper bound" (feedback-form pre-fill: newest overall); a value means "at or before this
    // instant" (admin-message-detail: what the user was looking at when they submitted). The
    // hits reader is identical either way.
    private async Task<SearchEventForPrefill?> ReadLatestSearchForSessionAsync(
        string sessionId,
        DateTime? atOrBeforeUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;

        var query = _dbContext.SearchEvents.Where(e => e.SessionId == sessionId);
        if (atOrBeforeUtc is { } bound)
        {
            query = query.Where(e => e.OccurredAtUtc <= bound);
        }

        var latest = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => new { e.Id, e.QueryRaw, e.QueryNormalised, e.ResultsTotal, e.OccurredAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
            return null;

        // Fetch the hits for that event in rendered order so the caller can show
        // "here are the results the user saw". Kept in-memory small (top 20 by Position);
        // that is more than any /search result page shows so nothing gets clipped visually,
        // and the sink already bounds impressions per event elsewhere.
        var hits = await _dbContext.SearchEventResults
            .Where(r => r.SearchEventId == latest.Id)
            .OrderBy(r => r.Position)
            .Take(20)
            .Select(r => new SearchHitPrefill(r.Position, r.ResultKind, r.ResultKey))
            .ToListAsync(cancellationToken);

        return new SearchEventForPrefill(
            latest.QueryRaw,
            latest.QueryNormalised,
            latest.ResultsTotal,
            latest.OccurredAtUtc,
            hits);
    }

    public Task<IReadOnlyList<VolumeBucket>> GetVolumeOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default) =>
        ReadVolumeAsync(fromUtc, toUtc, bucketSize, cancellationToken);

    public Task<IReadOnlyList<VolumeBucket>> GetUniqueSessionsOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<VolumeBucket>> GetZeroResultCountOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<LatencyBucket>> GetLatencyPercentilesOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<VolumeBucket>> GetVolumeOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        // Auto-pick the bucket granularity from the window width when no explicit size was
        // supplied. <=48h → hour; anything wider → day. Preserves the original default shape
        // for callers that have not adopted the explicit-bucket overload.
        var window = toUtc - fromUtc;
        var bucketSize = window > HourBucketThreshold ? VolumeBucketSize.Day : VolumeBucketSize.Hour;
        return ReadVolumeAsync(fromUtc, toUtc, bucketSize, cancellationToken);
    }

    // Shared read for both overloads. The SQL is templated on a per-bucket format string so
    // the same date_trunc/interval-literal pair applies to both the generate_series spine
    // and the counted-side bucket key — mis-aligned expressions would produce a spine whose
    // keys never join the counted rows and every bucket would render as zero.
    //
    // 15-minute is the one bucket Postgres date_trunc does not offer directly; it composes as
    // "date_trunc('hour', ts) + INTERVAL '15 minutes' * (EXTRACT(MINUTE FROM ts)::int / 15)".
    // The other four are single date_trunc calls with a matching interval literal for the step.
    private async Task<IReadOnlyList<VolumeBucket>> ReadVolumeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken)
    {
        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");

        var sql = $@"
SELECT
    b.bucket AS bucket,
    COALESCE(e.searches, 0)::int AS searches,
    COALESCE(e.unique_sessions, 0)::int AS unique_sessions
FROM generate_series(
    {spineStart},
    {spineEnd},
    {intervalLiteral}) AS b(bucket)
LEFT JOIN (
    SELECT
        {groupedBucketExpr} AS bucket,
        COUNT(*)::int AS searches,
        COUNT(DISTINCT session_id)::int AS unique_sessions
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY 1
) e ON e.bucket = b.bucket
WHERE b.bucket < @to
ORDER BY b.bucket;";

        var buckets = new List<VolumeBucket>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            buckets.Add(new VolumeBucket(bucket, reader.GetInt32(1), reader.GetInt32(2)));
        });

        return buckets;
    }

    // Maps a VolumeBucketSize to the pair of SQL fragments the read builds around:
    //   1. A composite format string with a single {0} placeholder — substitute a timestamptz
    //      expression (either an @-parameter reference or a column) and it produces the
    //      bucket-aligned key for that expression.
    //   2. The Postgres INTERVAL literal used as the generate_series step.
    // The 15-minute variant is the only one that cannot map to a single date_trunc unit; it
    // buckets to hour then adds a 15-minute multiple of (minute / 15).
    private static (string BucketExprFormat, string IntervalLiteral) BucketSqlPieces(VolumeBucketSize bucketSize) =>
        bucketSize switch
        {
            VolumeBucketSize.FifteenMinutes => (
                "date_trunc('hour', {0}) + INTERVAL '15 minutes' * (EXTRACT(MINUTE FROM {0})::int / 15)",
                "INTERVAL '15 minutes'"),
            VolumeBucketSize.Hour  => ("date_trunc('hour', {0})",  "INTERVAL '1 hour'"),
            VolumeBucketSize.Day   => ("date_trunc('day', {0})",   "INTERVAL '1 day'"),
            VolumeBucketSize.Week  => ("date_trunc('week', {0})",  "INTERVAL '1 week'"),
            VolumeBucketSize.Month => ("date_trunc('month', {0})", "INTERVAL '1 month'"),
            _ => throw new ArgumentOutOfRangeException(nameof(bucketSize), bucketSize, "Unknown bucket size."),
        };

    public async Task<(IReadOnlyList<TopQueryRow> Rows, int TotalCount)> GetPagedTopQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Defensive clamps: a page below 1 or a non-positive size would produce a negative
        // OFFSET or an unbounded LIMIT. The controller resolves pageSize from CMS:PageLength
        // and floors both — the query never trusts that either.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        return await ReadPagedTopQueriesAsync(fromUtc, toUtc, page, pageSize, zeroResultsOnly: false, cancellationToken);
    }

    public async Task<(IReadOnlyList<TopQueryRow> Rows, int TotalCount)> GetPagedTopZeroResultQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        return await ReadPagedTopQueriesAsync(fromUtc, toUtc, page, pageSize, zeroResultsOnly: true, cancellationToken);
    }

    // The COUNT and the page share the identical WHERE / GROUP BY so total-count and page-rows
    // never drift; zero_results = true is an additional predicate applied in the same place on
    // both queries so the paged zero-result reader inherits the exact same total shape.
    private async Task<(IReadOnlyList<TopQueryRow> Rows, int TotalCount)> ReadPagedTopQueriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        bool zeroResultsOnly,
        CancellationToken cancellationToken)
    {
        var extraFilter = zeroResultsOnly ? " AND zero_results = true" : string.Empty;

        var countSql = $@"
SELECT COUNT(*) FROM (
    SELECT query_normalised
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
      AND query_normalised IS NOT NULL{extraFilter}
    GROUP BY query_normalised
) q;";

        // In the zero-only case Count == ZeroResultCount by construction; keep the column for
        // shape parity with the non-paged reader so the view template does not branch.
        var zeroExpr = zeroResultsOnly
            ? "COUNT(*)::int"
            : "SUM(CASE WHEN zero_results THEN 1 ELSE 0 END)::int";

        var pageSql = $@"
SELECT
    query_normalised,
    COUNT(*)::int AS c,
    {zeroExpr} AS z
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
  AND query_normalised IS NOT NULL{extraFilter}
GROUP BY query_normalised
ORDER BY c DESC, query_normalised ASC
LIMIT @limit OFFSET @offset;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            total = Convert.ToInt32(reader.GetInt64(0));
        });

        var rows = new List<TopQueryRow>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            rows.Add(new TopQueryRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        });

        return (rows, total);
    }

    public async Task<(IReadOnlyList<TopPageRow> Rows, int TotalCount)> GetTopPagesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        // Joins result rows to their parent event so the window predicate applies to the parent's
        // occurred_at_utc. UniqueQueryCount is distinct parent events — "how many separate
        // searches surfaced this page in the window". Same tie-break (result_key ASC) as the
        // top-queries reader so the paged view is deterministic across identical queries.
        const string countSql = @"
SELECT COUNT(*) FROM (
    SELECT r.result_key
    FROM search_event_results r
    JOIN search_events e ON e.id = r.search_event_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
    GROUP BY r.result_key
) p;";

        const string pageSql = @"
SELECT
    r.result_key,
    COUNT(*)::int AS impressions,
    COUNT(DISTINCT r.search_event_id)::int AS unique_queries
FROM search_event_results r
JOIN search_events e ON e.id = r.search_event_id
WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
GROUP BY r.result_key
ORDER BY impressions DESC, r.result_key ASC
LIMIT @limit OFFSET @offset;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            total = Convert.ToInt32(reader.GetInt64(0));
        });

        var rows = new List<TopPageRow>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            rows.Add(new TopPageRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        });

        return (rows, total);
    }

    public async Task<IReadOnlyList<SessionHistoryRow>> GetSessionHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Array.Empty<SessionHistoryRow>();

        // LINQ read — no window predicate, so the session index carries the full lookup.
        // Ordered DESC so the drill-in reads newest-first, matching admin expectations
        // when scanning a session's history for the search that triggered a support note.
        var rows = await _dbContext.SearchEvents
            .AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => new SessionHistoryRow(
                e.OccurredAtUtc,
                e.QueryRaw,
                e.QueryNormalised,
                e.Scope,
                e.ResultsPages,
                e.ResultsBlocks,
                e.ResultsTotal,
                e.LatencyMs))
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => r with { OccurredAtUtc = DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc) })
            .ToList();
    }

    // Opens (or borrows) the DbContext's underlying Npgsql connection, runs the SQL, and
    // hands each row to the caller. Mirrors MetricsQueryService's helper — the two read
    // services share the layering pattern and a copy here keeps the sibling boundary
    // clean without introducing a shared helper class.
    private async Task ReadAsync(
        string sql,
        CancellationToken cancellationToken,
        Action<NpgsqlCommand> bindParameters,
        Action<NpgsqlDataReader> readRow)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bindParameters(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                readRow(reader);
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
