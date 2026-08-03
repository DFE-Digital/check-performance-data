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
        ReadSingleSeriesAsync(fromUtc, toUtc, bucketSize,
            countExpr: "COUNT(DISTINCT session_id)",
            zeroResultsOnly: false,
            cancellationToken);

    public Task<IReadOnlyList<VolumeBucket>> GetZeroResultCountOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default) =>
        ReadSingleSeriesAsync(fromUtc, toUtc, bucketSize,
            countExpr: "COUNT(*)",
            zeroResultsOnly: true,
            cancellationToken);

    // Shared read for the two single-series-per-bucket surfaces (unique users, zero-result
    // count). Same generate_series spine + LEFT JOIN gap-fill as ReadVolumeAsync; the count
    // expression and any extra WHERE predicate are the only per-caller differences. Value
    // lands in SearchCount so a single SVG partial can render either series.
    private async Task<IReadOnlyList<VolumeBucket>> ReadSingleSeriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        string countExpr,
        bool zeroResultsOnly,
        CancellationToken cancellationToken)
    {
        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");
        var extraWhere = zeroResultsOnly ? " AND zero_results = true" : string.Empty;

        var sql = $@"
SELECT
    b.bucket AS bucket,
    COALESCE(e.value, 0)::int AS value
FROM generate_series(
    {spineStart},
    {spineEnd},
    {intervalLiteral}) AS b(bucket)
LEFT JOIN (
    SELECT
        {groupedBucketExpr} AS bucket,
        {countExpr}::int AS value
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to{extraWhere}
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
            buckets.Add(new VolumeBucket(bucket, reader.GetInt32(1), 0));
        });

        return buckets;
    }

    // Continuous latency percentiles per bucket. percentile_cont(0.05|0.5|0.95) WITHIN GROUP
    // is Postgres's continuous percentile aggregate — interpolates between the two rows
    // surrounding the target percentile so the lines stay smooth as buckets fill. Empty
    // buckets aggregate to NULL server-side; COALESCE flips them to 0 so every bucket in
    // the spine emits a value (the chart client needs continuous polylines).
    public async Task<IReadOnlyList<LatencyBucket>> GetLatencyPercentilesOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        CancellationToken cancellationToken = default)
    {
        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");

        var sql = $@"
SELECT
    b.bucket AS bucket,
    COALESCE(e.p5,  0) AS p5,
    COALESCE(e.p50, 0) AS p50,
    COALESCE(e.p95, 0) AS p95
FROM generate_series(
    {spineStart},
    {spineEnd},
    {intervalLiteral}) AS b(bucket)
LEFT JOIN (
    SELECT
        {groupedBucketExpr} AS bucket,
        percentile_cont(0.05) WITHIN GROUP (ORDER BY latency_ms) AS p5,
        percentile_cont(0.50) WITHIN GROUP (ORDER BY latency_ms) AS p50,
        percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms) AS p95
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY 1
) e ON e.bucket = b.bucket
WHERE b.bucket < @to
ORDER BY b.bucket;";

        var buckets = new List<LatencyBucket>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var p5  = reader.IsDBNull(1) ? 0d : Convert.ToDouble(reader.GetValue(1));
            var p50 = reader.IsDBNull(2) ? 0d : Convert.ToDouble(reader.GetValue(2));
            var p95 = reader.IsDBNull(3) ? 0d : Convert.ToDouble(reader.GetValue(3));
            buckets.Add(new LatencyBucket(
                bucket,
                (int)Math.Round(p5,  MidpointRounding.AwayFromZero),
                (int)Math.Round(p50, MidpointRounding.AwayFromZero),
                (int)Math.Round(p95, MidpointRounding.AwayFromZero)));
        });

        return buckets;
    }

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
    // date_trunc(unit, timestamptz) evaluates in the DB session's TimeZone setting, so
    // bucket edges shift twice a year with DST if the session is not UTC. Wrap the
    // timestamptz in "AT TIME ZONE 'UTC'" first (converts to a plain timestamp on the UTC
    // clock face), date_trunc that, then wrap in "AT TIME ZONE 'UTC'" again (interprets
    // the truncated clock face AS UTC and returns a timestamptz). End result is a proper
    // timestamptz whose UTC value equals the truncated UTC of the input, so downstream
    // comparisons with @from / @to (also timestamptz) don't rely on session-TZ coercion.
    private static (string BucketExprFormat, string IntervalLiteral) BucketSqlPieces(VolumeBucketSize bucketSize) =>
        bucketSize switch
        {
            VolumeBucketSize.FifteenMinutes => (
                "(date_trunc('hour', {0} AT TIME ZONE 'UTC') + INTERVAL '15 minutes' * (EXTRACT(MINUTE FROM {0} AT TIME ZONE 'UTC')::int / 15)) AT TIME ZONE 'UTC'",
                "INTERVAL '15 minutes'"),
            VolumeBucketSize.Hour  => ("date_trunc('hour',  {0} AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'", "INTERVAL '1 hour'"),
            VolumeBucketSize.Day   => ("date_trunc('day',   {0} AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'", "INTERVAL '1 day'"),
            VolumeBucketSize.Week  => ("date_trunc('week',  {0} AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'", "INTERVAL '1 week'"),
            VolumeBucketSize.Month => ("date_trunc('month', {0} AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'", "INTERVAL '1 month'"),
            _ => throw new ArgumentOutOfRangeException(nameof(bucketSize), bucketSize, "Unknown bucket size."),
        };

    // Aggregate-to-typical-week readers. Collapses every event in the window into a 168-cell
    // cyclic 7-day timeline where cell (weekday, hour) sums / aggregates every event whose
    // occurred_at_utc landed on that weekday-of-week and hour-of-day, regardless of which
    // calendar week it fell in. BucketStart on each returned bucket is anchored to a fixed
    // synthetic Monday midnight (ANCHOR_MONDAY, 2001-01-01 UTC — a Monday) plus the
    // (weekday-1) * 24 + hour offset, so the X-axis renders "Mon 00:00 .. Sun 23:00" no
    // matter which weeks the raw events fell in.
    private static readonly DateTime ANCHOR_MONDAY = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task<IReadOnlyList<VolumeBucket>> GetVolumeAggregatedByWeekdayHourAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default) =>
        ReadWeekdayHourAggregateAsync(fromUtc, toUtc,
            countExpr: "COUNT(*)",
            zeroResultsOnly: false,
            cancellationToken);

    public Task<IReadOnlyList<VolumeBucket>> GetUniqueSessionsAggregatedByWeekdayHourAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default) =>
        ReadWeekdayHourAggregateAsync(fromUtc, toUtc,
            countExpr: "COUNT(DISTINCT session_id)",
            zeroResultsOnly: false,
            cancellationToken);

    public Task<IReadOnlyList<VolumeBucket>> GetZeroResultCountAggregatedByWeekdayHourAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default) =>
        ReadWeekdayHourAggregateAsync(fromUtc, toUtc,
            countExpr: "COUNT(*)",
            zeroResultsOnly: true,
            cancellationToken);

    private async Task<IReadOnlyList<VolumeBucket>> ReadWeekdayHourAggregateAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string countExpr,
        bool zeroResultsOnly,
        CancellationToken cancellationToken)
    {
        var extraWhere = zeroResultsOnly ? " AND zero_results = true" : string.Empty;

        // ISO weekday: 1=Mon..7=Sun. hour: 0..23. Group by that pair and gap-fill via a
        // 7 x 24 spine so cells without events render as 0 on the chart.
        var sql = $@"
SELECT
    w.weekday::int AS weekday,
    hh.hour::int   AS hour,
    COALESCE(e.value, 0)::int AS value
FROM generate_series(1, 7) AS w(weekday)
CROSS JOIN generate_series(0, 23) AS hh(hour)
LEFT JOIN (
    SELECT
        EXTRACT(ISODOW FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS weekday,
        EXTRACT(HOUR   FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS hour,
        {countExpr}::int AS value
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to{extraWhere}
    GROUP BY 1, 2
) e ON e.weekday = w.weekday AND e.hour = hh.hour
ORDER BY w.weekday, hh.hour;";

        var buckets = new List<VolumeBucket>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var weekday = reader.GetInt32(0);
            var hour = reader.GetInt32(1);
            var value = reader.GetInt32(2);
            var bucketStart = ANCHOR_MONDAY.AddDays(weekday - 1).AddHours(hour);
            buckets.Add(new VolumeBucket(bucketStart, value, 0));
        });

        return buckets;
    }

    public async Task<IReadOnlyList<LatencyBucket>> GetLatencyPercentilesAggregatedByWeekdayHourAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
SELECT
    w.weekday::int AS weekday,
    hh.hour::int   AS hour,
    COALESCE(e.p5,  0) AS p5,
    COALESCE(e.p50, 0) AS p50,
    COALESCE(e.p95, 0) AS p95
FROM generate_series(1, 7) AS w(weekday)
CROSS JOIN generate_series(0, 23) AS hh(hour)
LEFT JOIN (
    SELECT
        EXTRACT(ISODOW FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS weekday,
        EXTRACT(HOUR   FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS hour,
        percentile_cont(0.05) WITHIN GROUP (ORDER BY latency_ms) AS p5,
        percentile_cont(0.50) WITHIN GROUP (ORDER BY latency_ms) AS p50,
        percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms) AS p95
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY 1, 2
) e ON e.weekday = w.weekday AND e.hour = hh.hour
ORDER BY w.weekday, hh.hour;";

        var buckets = new List<LatencyBucket>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var weekday = reader.GetInt32(0);
            var hour = reader.GetInt32(1);
            var p5  = reader.IsDBNull(2) ? 0d : Convert.ToDouble(reader.GetValue(2));
            var p50 = reader.IsDBNull(3) ? 0d : Convert.ToDouble(reader.GetValue(3));
            var p95 = reader.IsDBNull(4) ? 0d : Convert.ToDouble(reader.GetValue(4));
            var bucketStart = ANCHOR_MONDAY.AddDays(weekday - 1).AddHours(hour);
            buckets.Add(new LatencyBucket(
                bucketStart,
                (int)Math.Round(p5,  MidpointRounding.AwayFromZero),
                (int)Math.Round(p50, MidpointRounding.AwayFromZero),
                (int)Math.Round(p95, MidpointRounding.AwayFromZero)));
        });

        return buckets;
    }

    // Paged variant of the volume reader. Same generate_series spine as ReadVolumeAsync so
    // gap-fill and total-bucket-count stay consistent with the landing-page chart; a COUNT
    // over the spine feeds the pager's total. LIMIT/OFFSET slice the ordered spine so the
    // pager on the drill-in table works over the same rows the chart above renders.
    public Task<(IReadOnlyList<VolumeBucket> Rows, int TotalCount)> GetPagedVolumeOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadPagedVolumeAsync(fromUtc, toUtc, bucketSize, page, pageSize, cancellationToken);

    public Task<(IReadOnlyList<VolumeBucket> Rows, int TotalCount)> GetPagedUniqueSessionsOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadPagedSingleSeriesAsync(fromUtc, toUtc, bucketSize, page, pageSize,
            countExpr: "COUNT(DISTINCT session_id)",
            zeroResultsOnly: false,
            cancellationToken);

    public Task<(IReadOnlyList<VolumeBucket> Rows, int TotalCount)> GetPagedZeroResultCountOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadPagedSingleSeriesAsync(fromUtc, toUtc, bucketSize, page, pageSize,
            countExpr: "COUNT(*)",
            zeroResultsOnly: true,
            cancellationToken);

    public Task<(IReadOnlyList<LatencyBucket> Rows, int TotalCount)> GetPagedLatencyPercentilesOverTimeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadPagedLatencyPercentilesAsync(fromUtc, toUtc, bucketSize, page, pageSize, cancellationToken);

    // Shared paged read for the dual-axis volume series. First runs the same spine SQL as
    // ReadVolumeAsync but wrapped in a COUNT so the total bucket count is known before the
    // slice query. Slice query adds LIMIT/OFFSET over the same spine ordering.
    private async Task<(IReadOnlyList<VolumeBucket>, int)> ReadPagedVolumeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");

        var countSql = $@"
SELECT COUNT(*) FROM (
    SELECT b.bucket
    FROM generate_series({spineStart}, {spineEnd}, {intervalLiteral}) AS b(bucket)
    WHERE b.bucket < @to
) x;";

        var pageSql = $@"
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
ORDER BY b.bucket
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

        var rows = new List<VolumeBucket>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            rows.Add(new VolumeBucket(bucket, reader.GetInt32(1), reader.GetInt32(2)));
        });

        return (rows, total);
    }

    // Shared paged read for the two single-series drill-ins (unique users, zero-result count).
    // Mirrors ReadSingleSeriesAsync but adds a count-over-spine + LIMIT/OFFSET slice.
    private async Task<(IReadOnlyList<VolumeBucket>, int)> ReadPagedSingleSeriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        string countExpr,
        bool zeroResultsOnly,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");
        var extraWhere = zeroResultsOnly ? " AND zero_results = true" : string.Empty;

        var countSql = $@"
SELECT COUNT(*) FROM (
    SELECT b.bucket
    FROM generate_series({spineStart}, {spineEnd}, {intervalLiteral}) AS b(bucket)
    WHERE b.bucket < @to
) x;";

        var pageSql = $@"
SELECT
    b.bucket AS bucket,
    COALESCE(e.value, 0)::int AS value
FROM generate_series(
    {spineStart},
    {spineEnd},
    {intervalLiteral}) AS b(bucket)
LEFT JOIN (
    SELECT
        {groupedBucketExpr} AS bucket,
        {countExpr}::int AS value
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to{extraWhere}
    GROUP BY 1
) e ON e.bucket = b.bucket
WHERE b.bucket < @to
ORDER BY b.bucket
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

        var rows = new List<VolumeBucket>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            rows.Add(new VolumeBucket(bucket, reader.GetInt32(1), 0));
        });

        return (rows, total);
    }

    // Paged latency-percentiles reader. Same generate_series spine + percentile_cont as
    // the non-paged reader, sliced by LIMIT/OFFSET so the drill-in pager works over identical
    // rows to the chart above.
    private async Task<(IReadOnlyList<LatencyBucket>, int)> ReadPagedLatencyPercentilesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        VolumeBucketSize bucketSize,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        var (bucketExprFmt, intervalLiteral) = BucketSqlPieces(bucketSize);
        var spineStart = string.Format(bucketExprFmt, "@from");
        var spineEnd = string.Format(bucketExprFmt, "@to");
        var groupedBucketExpr = string.Format(bucketExprFmt, "occurred_at_utc");

        var countSql = $@"
SELECT COUNT(*) FROM (
    SELECT b.bucket
    FROM generate_series({spineStart}, {spineEnd}, {intervalLiteral}) AS b(bucket)
    WHERE b.bucket < @to
) x;";

        var pageSql = $@"
SELECT
    b.bucket AS bucket,
    COALESCE(e.p5,  0) AS p5,
    COALESCE(e.p50, 0) AS p50,
    COALESCE(e.p95, 0) AS p95
FROM generate_series(
    {spineStart},
    {spineEnd},
    {intervalLiteral}) AS b(bucket)
LEFT JOIN (
    SELECT
        {groupedBucketExpr} AS bucket,
        percentile_cont(0.05) WITHIN GROUP (ORDER BY latency_ms) AS p5,
        percentile_cont(0.50) WITHIN GROUP (ORDER BY latency_ms) AS p50,
        percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms) AS p95
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY 1
) e ON e.bucket = b.bucket
WHERE b.bucket < @to
ORDER BY b.bucket
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

        var rows = new List<LatencyBucket>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var p5  = reader.IsDBNull(1) ? 0d : Convert.ToDouble(reader.GetValue(1));
            var p50 = reader.IsDBNull(2) ? 0d : Convert.ToDouble(reader.GetValue(2));
            var p95 = reader.IsDBNull(3) ? 0d : Convert.ToDouble(reader.GetValue(3));
            rows.Add(new LatencyBucket(
                bucket,
                (int)Math.Round(p5,  MidpointRounding.AwayFromZero),
                (int)Math.Round(p50, MidpointRounding.AwayFromZero),
                (int)Math.Round(p95, MidpointRounding.AwayFromZero)));
        });

        return (rows, total);
    }

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

    public Task<(IReadOnlyList<TopPageRow> Rows, int TotalCount)> GetTopPagesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadTopHitsByKindAsync(fromUtc, toUtc, "page", page, pageSize, cancellationToken);

    public Task<(IReadOnlyList<TopPageRow> Rows, int TotalCount)> GetTopBlocksAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ReadTopHitsByKindAsync(fromUtc, toUtc, "block", page, pageSize, cancellationToken);

    // Shared reader behind GetTopPages/GetTopBlocks. Splitting on result_kind is important
    // because pages and blocks live in the same table but are semantically different — a
    // page is a URL you can navigate to, a block is a snippet that gets rendered on any
    // number of host pages. Grouping them together would produce a "Top pages" card with
    // non-navigable entries mid-list, which is exactly the bug this method's callers avoid.
    private async Task<(IReadOnlyList<TopPageRow> Rows, int TotalCount)> ReadTopHitsByKindAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string kind,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        const string countSql = @"
SELECT COUNT(*) FROM (
    SELECT r.result_key
    FROM search_event_results r
    JOIN search_events e ON e.id = r.search_event_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
      AND r.result_kind = @kind
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
  AND r.result_kind = @kind
GROUP BY r.result_key
ORDER BY impressions DESC, r.result_key ASC
LIMIT @limit OFFSET @offset;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = kind });
        }, reader =>
        {
            total = Convert.ToInt32(reader.GetInt64(0));
        });

        var rows = new List<TopPageRow>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = kind });
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

    // Request-timings scatter chart feed. When the window's event count exceeds
    // samplingLimit the query samples via ORDER BY random() so the chart still gets a
    // representative spread across the window (rather than the first N events, which
    // would clip the plot to a fraction of the X-axis). When under the limit every event
    // is returned.
    public async Task<IReadOnlyList<RequestTimingPoint>> GetRequestTimingsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int samplingLimit,
        CancellationToken cancellationToken = default)
    {
        if (samplingLimit < 1) samplingLimit = 1;

        const string sql = @"
SELECT occurred_at_utc, latency_ms, session_id, query_raw, results_total
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
ORDER BY random()
LIMIT @limit;";

        var points = new List<RequestTimingPoint>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = samplingLimit });
        }, reader =>
        {
            var occurredAt = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var latency = reader.GetInt32(1);
            var sessionId = reader.GetString(2);
            var query = reader.IsDBNull(3) ? null : reader.GetString(3);
            var resultsTotal = reader.GetInt32(4);
            points.Add(new RequestTimingPoint(occurredAt, latency, sessionId, query, resultsTotal));
        });

        return points;
    }

    // Paged request-timings drill-in. Newest first so the top of page 1 is the most
    // recent event; the pager runs over the total window count. No sampling.
    public async Task<(IReadOnlyList<RequestTimingPoint> Rows, int TotalCount)> GetPagedRequestTimingsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        const string countSql = @"
SELECT COUNT(*)::int
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to;";

        const string pageSql = @"
SELECT occurred_at_utc, latency_ms, session_id, query_raw, results_total
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
ORDER BY occurred_at_utc DESC, id DESC
LIMIT @limit OFFSET @offset;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            total = reader.GetInt32(0);
        });

        var rows = new List<RequestTimingPoint>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var occurredAt = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var latency = reader.GetInt32(1);
            var sessionId = reader.GetString(2);
            var query = reader.IsDBNull(3) ? null : reader.GetString(3);
            var resultsTotal = reader.GetInt32(4);
            rows.Add(new RequestTimingPoint(occurredAt, latency, sessionId, query, resultsTotal));
        });

        return (rows, total);
    }

    public async Task<(IReadOnlyList<RequestTimingPoint> Rows, int TotalCount)> GetPagedEventsInWeekdayHourBucketAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int weekday,
        int hour,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        // Server-side clamp: EXTRACT(ISODOW) is 1..7 and EXTRACT(HOUR) is 0..23. An
        // out-of-band value never matches any row so the query returns empty — but
        // let's not send garbage to Postgres.
        if (weekday < 1 || weekday > 7 || hour < 0 || hour > 23)
        {
            return (Array.Empty<RequestTimingPoint>(), 0);
        }

        // EXTRACT on a timestamptz converts to the DB session's TimeZone setting BEFORE
        // extracting the field. The connection string sets no TimeZone parameter, so on a
        // Postgres whose session default is Europe/London the same URL returns different
        // rows twice a year across the BST/GMT switch. AT TIME ZONE 'UTC' pins the field
        // extraction to UTC clock-face values regardless of the session's default.
        const string countSql = @"
SELECT COUNT(*)::int
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
  AND EXTRACT(ISODOW FROM occurred_at_utc AT TIME ZONE 'UTC')::int = @weekday
  AND EXTRACT(HOUR   FROM occurred_at_utc AT TIME ZONE 'UTC')::int = @hour;";

        const string pageSql = @"
SELECT occurred_at_utc, latency_ms, session_id, query_raw, results_total
FROM search_events
WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
  AND EXTRACT(ISODOW FROM occurred_at_utc AT TIME ZONE 'UTC')::int = @weekday
  AND EXTRACT(HOUR   FROM occurred_at_utc AT TIME ZONE 'UTC')::int = @hour
ORDER BY occurred_at_utc DESC, id DESC
LIMIT @limit OFFSET @offset;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("weekday", NpgsqlDbType.Integer) { Value = weekday });
            command.Parameters.Add(new NpgsqlParameter("hour", NpgsqlDbType.Integer) { Value = hour });
        }, reader =>
        {
            total = reader.GetInt32(0);
        });

        var rows = new List<RequestTimingPoint>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("weekday", NpgsqlDbType.Integer) { Value = weekday });
            command.Parameters.Add(new NpgsqlParameter("hour", NpgsqlDbType.Integer) { Value = hour });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var occurredAt = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var latency = reader.GetInt32(1);
            var sessionId = reader.GetString(2);
            var query = reader.IsDBNull(3) ? null : reader.GetString(3);
            var resultsTotal = reader.GetInt32(4);
            rows.Add(new RequestTimingPoint(occurredAt, latency, sessionId, query, resultsTotal));
        });

        return (rows, total);
    }

    // Session-level recovery stats for zero-result-having sessions. Three CTEs feed a
    // single aggregate:
    //   * sessions_with_zero — every session id that had at least one zero-result event
    //     in the window (BOOL_OR aggregation).
    //   * recovered — sessions above that later received at least one >0-result event
    //     inside the window.
    //   * feedback — sessions above that filed at least one search_messages row inside
    //     the window.
    // Feedback and recovery are non-exclusive: a session can both refine successfully
    // AND send feedback. Abandoned = SessionsWithAnyZeroResult - Recovered - (feedback-
    // only slice) is computed by the caller so this reader stays close to the shape of
    // its underlying data.
    public async Task<ZeroResultRecoveryStats> GetZeroResultRecoveryStatsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        // "Recovered" requires a successful event AFTER the session's first zero-result,
        // not any successful event in the window. Without the ordering guard, a session
        // that ran a success at 10:00 and then a zero-result at 11:00 would be labelled
        // recovered even though the last thing they did was fail — and the sibling
        // GetZeroResultOutcomeFunnelAsync (which does enforce the ordering) would report
        // a lower "refined" figure for the same window, so the two cards disagreed.
        const string sql = @"
WITH first_zero AS (
    SELECT session_id, MIN(occurred_at_utc) AS first_zero_utc
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
      AND zero_results
    GROUP BY session_id
),
recovered AS (
    SELECT DISTINCT fz.session_id
    FROM first_zero fz
    JOIN search_events e ON e.session_id = fz.session_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
      AND e.occurred_at_utc > fz.first_zero_utc
      AND NOT e.zero_results
),
feedback AS (
    SELECT DISTINCT fz.session_id
    FROM first_zero fz
    JOIN search_messages m ON m.session_id = fz.session_id
    WHERE m.submitted_at_utc >= @from AND m.submitted_at_utc < @to
      AND m.submitted_at_utc > fz.first_zero_utc
)
SELECT
    (SELECT COUNT(*) FROM first_zero)::int AS total_sessions,
    (SELECT COUNT(*) FROM recovered)::int  AS recovered_count,
    (SELECT COUNT(*) FROM feedback)::int   AS feedback_count;";

        var total = 0;
        var recovered = 0;
        var feedback = 0;
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            total = reader.GetInt32(0);
            recovered = reader.GetInt32(1);
            feedback = reader.GetInt32(2);
        });

        // "Abandoned" = neither recovered NOR sent feedback. The aggregate above doesn't
        // give us the recovered∩feedback intersection, so take a conservative upper bound
        // on "did not abandon" (recovered + feedback capped at total). This slightly
        // understates abandoned when a session both recovered AND sent feedback, but
        // the drill-in surfaces the precise state per session so the headline can stay
        // simple + non-alarming.
        var didNotAbandon = Math.Min(total, recovered + feedback);
        var abandoned = Math.Max(0, total - didNotAbandon);

        return new ZeroResultRecoveryStats(total, recovered, abandoned, feedback);
    }

    // Paged list of zero-result-having sessions with each session's full search chain
    // in the window (oldest-first). Two-query shape: aggregate count, then a JOIN that
    // fetches every event for the paged slice of session_ids in one round-trip so the
    // chain assembly stays O(N) client-side. Ordering: chain length DESC, session_id
    // ASC — long struggles surface first, deterministic tie-break.
    public async Task<(IReadOnlyList<ZeroResultJourney> Rows, int TotalCount)> GetZeroResultJourneysAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        const string countSql = @"
SELECT COUNT(*)::int FROM (
    SELECT session_id
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY session_id
    HAVING BOOL_OR(zero_results)
) s;";

        // Rank sessions by their in-window chain length (event count) desc, id asc, take
        // the requested page, then fan out to every event for the ids on that page. One
        // ORDER BY at the outer SELECT gives per-session chronological chains.
        const string pageSql = @"
WITH ranked AS (
    SELECT session_id, COUNT(*) AS chain_length
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY session_id
    HAVING BOOL_OR(zero_results)
    ORDER BY COUNT(*) DESC, session_id ASC
    LIMIT @limit OFFSET @offset
),
page_sessions AS (
    SELECT session_id FROM ranked
),
events AS (
    SELECT e.session_id, e.occurred_at_utc, e.query_normalised, e.results_total,
           e.zero_results
    FROM search_events e
    JOIN page_sessions p ON p.session_id = e.session_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
),
feedback AS (
    SELECT DISTINCT m.session_id
    FROM search_messages m
    JOIN page_sessions p ON p.session_id = m.session_id
    WHERE m.submitted_at_utc >= @from AND m.submitted_at_utc < @to
),
first_zero AS (
    SELECT p.session_id, MIN(e.occurred_at_utc) AS first_zero_utc
    FROM search_events e
    JOIN page_sessions p ON p.session_id = e.session_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
      AND e.zero_results
    GROUP BY p.session_id
),
recovered AS (
    SELECT DISTINCT fz.session_id
    FROM first_zero fz
    JOIN search_events e ON e.session_id = fz.session_id
    WHERE e.occurred_at_utc >= @from AND e.occurred_at_utc < @to
      AND e.occurred_at_utc > fz.first_zero_utc
      AND NOT e.zero_results
)
SELECT
    e.session_id,
    e.occurred_at_utc,
    e.query_normalised,
    e.results_total,
    (r.session_id IS NOT NULL) AS recovered,
    (f.session_id IS NOT NULL) AS sent_feedback
FROM events e
LEFT JOIN recovered r ON r.session_id = e.session_id
LEFT JOIN feedback f ON f.session_id = e.session_id
ORDER BY (SELECT chain_length FROM ranked WHERE ranked.session_id = e.session_id) DESC,
         e.session_id ASC,
         e.occurred_at_utc ASC;";

        var total = 0;
        await ReadAsync(countSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            total = reader.GetInt32(0);
        });

        var journeysBySession = new Dictionary<string, (List<RefinementStep> Steps, bool Recovered, bool Sent)>();
        var order = new List<string>();
        await ReadAsync(pageSql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = pageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = (page - 1) * pageSize });
        }, reader =>
        {
            var sessionId = reader.GetString(0);
            var occurredAt = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            var query = reader.IsDBNull(2) ? null : reader.GetString(2);
            var results = reader.GetInt32(3);
            var recovered = reader.GetBoolean(4);
            var sent = reader.GetBoolean(5);
            if (!journeysBySession.TryGetValue(sessionId, out var acc))
            {
                acc = (new List<RefinementStep>(), recovered, sent);
                journeysBySession[sessionId] = acc;
                order.Add(sessionId);
            }
            acc.Steps.Add(new RefinementStep(occurredAt, query, results));
        });

        var rows = order
            .Select(id =>
            {
                var acc = journeysBySession[id];
                return new ZeroResultJourney(id, acc.Steps, acc.Recovered, acc.Sent);
            })
            .ToList();
        return (rows, total);
    }

    // Weekday × hour-of-day search volume heatmap. Postgres EXTRACT(ISODOW) returns
    // 1..7 with Monday=1 (matches the UK-audience week-start convention). EXTRACT on a
    // timestamptz converts to the DB session's TimeZone BEFORE extracting the field, so
    // occurred_at_utc AT TIME ZONE 'UTC' pins the extraction to UTC regardless of the
    // Postgres session's default. A generate_series-based CROSS JOIN produces the
    // 168-cell spine; LEFT JOIN gap-fills empty cells to zero so the SVG grid has no
    // holes.
    public async Task<IReadOnlyList<WeekdayHourBucket>> GetSearchesByWeekdayAndHourAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    w.weekday::int AS weekday,
    h.hour::int AS hour,
    COALESCE(e.c, 0)::int AS c
FROM generate_series(1, 7) AS w(weekday)
CROSS JOIN generate_series(0, 23) AS h(hour)
LEFT JOIN (
    SELECT
        EXTRACT(ISODOW FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS weekday,
        EXTRACT(HOUR   FROM occurred_at_utc AT TIME ZONE 'UTC')::int AS hour,
        COUNT(*)::int AS c
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
    GROUP BY 1, 2
) e ON e.weekday = w.weekday AND e.hour = h.hour
ORDER BY w.weekday, h.hour;";

        var buckets = new List<WeekdayHourBucket>(168);
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            buckets.Add(new WeekdayHourBucket(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        });

        return buckets;
    }

    // Session-level "what happened after a zero-result search" rollup for the funnel
    // card. Everything is one aggregate CTE-driven query so the tile computes in one
    // round-trip. Tie-break: refined > feedback > silent (a query change is a stronger
    // signal of continued intent than a message that may not even be about the same
    // topic).
    public async Task<ZeroResultOutcomeSummary> GetZeroResultOutcomeFunnelAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
WITH first_zero AS (
    -- DISTINCT ON rather than MIN(): the baseline must be the query that actually ran
    -- first, and MIN(query_normalised) is the alphabetically smallest zero-result query
    -- in the session instead. With the wrong baseline a genuine refinement can compare
    -- equal to it and be misfiled as silent, which undercounted refined sessions and put
    -- this card at odds with the journeys drill-in. id breaks ties on identical instants
    -- so the pick is deterministic.
    SELECT DISTINCT ON (session_id)
           session_id,
           occurred_at_utc  AS first_zero_utc,
           query_normalised AS first_zero_query
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
      AND zero_results = true
    ORDER BY session_id, occurred_at_utc, id
),
refined AS (
    SELECT DISTINCT fz.session_id
    FROM first_zero fz
    JOIN search_events e ON e.session_id = fz.session_id
    WHERE e.occurred_at_utc > fz.first_zero_utc
      AND e.occurred_at_utc < @to
      AND (e.query_normalised IS DISTINCT FROM fz.first_zero_query)
),
feedback AS (
    SELECT DISTINCT fz.session_id
    FROM first_zero fz
    JOIN search_messages m ON m.session_id = fz.session_id
    WHERE m.submitted_at_utc > fz.first_zero_utc
      AND m.submitted_at_utc < @to
),
classified AS (
    SELECT fz.session_id,
           CASE
               WHEN r.session_id IS NOT NULL THEN 'refined'
               WHEN f.session_id IS NOT NULL THEN 'feedback'
               ELSE 'silent'
           END AS outcome
    FROM first_zero fz
    LEFT JOIN refined  r ON r.session_id = fz.session_id
    LEFT JOIN feedback f ON f.session_id = fz.session_id
),
all_sessions AS (
    SELECT COUNT(DISTINCT session_id)::int AS c
    FROM search_events
    WHERE occurred_at_utc >= @from AND occurred_at_utc < @to
)
SELECT
    (SELECT COUNT(*)::int FROM classified)                              AS total_zero,
    (SELECT c FROM all_sessions)                                        AS total_sessions,
    (SELECT COUNT(*)::int FROM classified WHERE outcome = 'refined')    AS refined_count,
    (SELECT COUNT(*)::int FROM classified WHERE outcome = 'feedback')   AS feedback_count,
    (SELECT COUNT(*)::int FROM classified WHERE outcome = 'silent')     AS silent_count;";

        var totalZero = 0;
        var totalSessions = 0;
        var refinedCount = 0;
        var feedbackCount = 0;
        var silentCount = 0;
        var found = false;

        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            totalZero = reader.GetInt32(0);
            totalSessions = reader.GetInt32(1);
            refinedCount = reader.GetInt32(2);
            feedbackCount = reader.GetInt32(3);
            silentCount = reader.GetInt32(4);
            found = true;
        });

        if (!found)
            return new ZeroResultOutcomeSummary(0, 0, 0, 0, 0);

        return new ZeroResultOutcomeSummary(
            totalZero,
            totalSessions,
            refinedCount,
            feedbackCount,
            silentCount);
    }

    // Prior-window summary alongside the current-window one. Prior window ends at
    // fromUtc and is the same width as (toUtc - fromUtc). When the resulting priorFrom
    // falls outside the sink's 90-day retention floor the summary is unavailable and the
    // view hides the four anomaly chips (no valid comparison to draw).
    public async Task<SearchAnalyticsSummaryDeltas> GetSummaryDeltasAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var width = toUtc - fromUtc;
        if (width <= TimeSpan.Zero)
        {
            return new SearchAnalyticsSummaryDeltas(0, 0, 0, 0, Available: false);
        }

        var priorFrom = fromUtc - width;
        var priorTo = fromUtc;
        var retentionFloor = DateTime.UtcNow - TimeSpan.FromDays(90);
        if (priorFrom < retentionFloor)
        {
            return new SearchAnalyticsSummaryDeltas(0, 0, 0, 0, Available: false);
        }

        var priorSummary = await GetSummaryAsync(priorFrom, priorTo, cancellationToken);
        return new SearchAnalyticsSummaryDeltas(
            priorSummary.TotalCount,
            priorSummary.UniqueSessions,
            priorSummary.ZeroResultRatePercent,
            priorSummary.P95LatencyMs,
            Available: true);
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
