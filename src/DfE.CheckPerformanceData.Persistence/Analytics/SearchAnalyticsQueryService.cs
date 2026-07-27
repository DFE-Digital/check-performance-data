using System.Data;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Reads search_events for the search-analytics admin dashboard. Aggregation is
// database-side: a single window query returns total / distinct-sessions / zero-count /
// p95 for the tiles; grouped queries return the top-N normalised queries and the top-N
// zero-result queries; a cheap COUNT gates the empty-state; and a LINQ read returns the
// newest event for a given session id (feedback-form pre-fill). Every value crosses as
// an Npgsql parameter, so the raw SQL has no injection or interpolation path.
//
// Interface lives in Application; the implementation lives here because it reads the
// shared DbContext directly via raw Npgsql (same layering used for IMetricsSink /
// DbMetricsSink and MetricsQueryService). The Application project cannot reference
// Persistence, so the database-touching read side belongs on this side of the boundary.
public sealed class SearchAnalyticsQueryService : ISearchAnalyticsQueryService
{
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

    public async Task<SearchEventForPrefill?> GetLatestSearchForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;

        var row = await _dbContext.SearchEvents
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => new SearchEventForPrefill(
                e.QueryRaw,
                e.QueryNormalised,
                e.ResultsTotal,
                e.OccurredAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
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
