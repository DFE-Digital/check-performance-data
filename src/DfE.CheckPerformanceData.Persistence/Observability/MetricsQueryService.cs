using System.Data;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Observability;

// Reads queue_metrics_events for every downstream observability surface. Aggregation is
// database-side: a generate_series spine LEFT JOINed onto a grouped count gap-fills empty
// buckets to zero. The bucket key is derived from the ThroughputGranularity allow-list (a
// date_trunc unit for the calendar granularities, floor-of-epoch arithmetic for the derived
// 5/10-minute ones) and never from a caller string, and every value crosses as an Npgsql
// parameter, so the raw SQL has no injection or interpolation path. The date range is bounded
// to keep an abusive "ten years of per-second buckets" query from scanning the whole table.
//
// The interface lives in the Application layer; the implementation lives here because it reads
// the shared DbContext directly via raw Npgsql (the same layering used for IMetricsSink /
// DbMetricsSink). The Application project cannot reference Persistence (Persistence references
// Application), so the database-touching read side belongs on this side of the boundary.
public sealed class MetricsQueryService : IMetricsQueryService
{
    // The widest a single aggregation request may span. Generous enough for "last 90 days at
    // a daily granularity" while making a decade-of-seconds request impossible.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    private readonly IPortalDbContext _dbContext;

    public MetricsQueryService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ThroughputBucket>> GetThroughputAsync(
        string queueName,
        ThroughputGranularity granularity,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var spec = BucketSpec.For(granularity); // throws ArgumentException for an unknown value
        GuardRange(fromUtc, toUtc);

        var bucketExpr = spec.BucketExpression("recorded_at_utc");

        // generate_series builds one row per bucket across the range; the LEFT JOIN onto the
        // grouped counts gap-fills the empties to zero. The bucket expression is identical on
        // both sides so the join keys line up exactly.
        var sql = $@"
SELECT b.bucket AS bucket, COALESCE(e.cnt, 0) AS cnt
FROM generate_series(@from, @to - @step, @step) AS b(bucket)
LEFT JOIN (
    SELECT {bucketExpr} AS bucket, COUNT(*) AS cnt
    FROM queue_metrics_events
    WHERE recorded_at_utc >= @from AND recorded_at_utc < @to AND queue_name = @queue
    GROUP BY 1
) e ON e.bucket = b.bucket
ORDER BY b.bucket;";

        var results = new List<ThroughputBucket>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
            command.Parameters.Add(new NpgsqlParameter("step", NpgsqlDbType.Interval) { Value = spec.Interval });
            command.Parameters.Add(new NpgsqlParameter("queue", NpgsqlDbType.Text) { Value = queueName });
        }, reader =>
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var count = Convert.ToInt32(reader.GetInt64(1));
            results.Add(new ThroughputBucket(bucket, count));
        });

        return results;
    }

    public async Task<IReadOnlyList<StageDwell>> GetDwellByStageAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        GuardRange(fromUtc, toUtc);

        const string sql = @"
SELECT stage, AVG(latency_ms) AS avg_latency
FROM queue_metrics_events
WHERE recorded_at_utc >= @from AND recorded_at_utc < @to
GROUP BY stage
ORDER BY stage;";

        var results = new List<StageDwell>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var stage = reader.GetString(0);
            var avg = reader.IsDBNull(1) ? 0d : Convert.ToDouble(reader.GetValue(1));
            results.Add(new StageDwell(stage, avg));
        });

        return results;
    }

    public async Task<IReadOnlyList<DecisionMixEntry>> GetDecisionMixAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        GuardRange(fromUtc, toUtc);

        const string sql = @"
SELECT decision_status, COUNT(*) AS cnt
FROM queue_metrics_events
WHERE recorded_at_utc >= @from AND recorded_at_utc < @to AND decision_status IS NOT NULL
GROUP BY decision_status
ORDER BY decision_status;";

        var results = new List<DecisionMixEntry>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc });
        }, reader =>
        {
            var status = reader.GetString(0);
            var count = Convert.ToInt32(reader.GetInt64(1));
            results.Add(new DecisionMixEntry(status, count));
        });

        return results;
    }

    public async Task<IReadOnlyList<JourneyEvent>> GetJourneyAsync(
        string referenceNumber,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT stage, reference_number, queue_name, decision_status, latency_ms, recorded_at_utc
FROM queue_metrics_events
WHERE reference_number = @reference
ORDER BY recorded_at_utc;";

        var results = new List<JourneyEvent>();
        await ReadAsync(sql, cancellationToken, command =>
        {
            command.Parameters.Add(new NpgsqlParameter("reference", NpgsqlDbType.Text) { Value = referenceNumber });
        }, reader =>
        {
            results.Add(new JourneyEvent(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDouble(4),
                DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
        });

        return results;
    }

    public async Task<IReadOnlyList<DeployMarker>> GetDeployMarkersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        GuardRange(fromUtc, toUtc);

        var versions = await _dbContext.RulesConfigVersions
            .Where(v => v.CreatedAt >= fromUtc && v.CreatedAt < toUtc)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new { v.CreatedAt, v.VersionNumber })
            .ToListAsync(cancellationToken);

        return versions
            .Select(v => new DeployMarker(
                DateTime.SpecifyKind(v.CreatedAt, DateTimeKind.Utc),
                $"rules v{v.VersionNumber}"))
            .ToList();
    }

    public async Task<ObservabilitySnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var depths = await _dbContext.QueueMessages
            .GroupBy(m => m.QueueName)
            .Select(g => new
            {
                QueueName = g.Key,
                Depth = g.Count(),
                Oldest = g.Min(m => (DateTime?)m.EnqueuedAtUtc),
            })
            .ToListAsync(cancellationToken);

        var depthSnapshots = depths
            .Select(d => new QueueDepthSnapshot(
                d.QueueName,
                d.Depth,
                d.Oldest is null ? null : now - d.Oldest.Value))
            .ToList();

        var recent = await _dbContext.QueueMetricEvents
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(20)
            .Select(e => new JourneyEvent(
                e.Stage,
                e.ReferenceNumber,
                e.QueueName,
                e.DecisionStatus,
                e.LatencyMs,
                e.RecordedAtUtc))
            .ToListAsync(cancellationToken);

        var decisionMix = await _dbContext.QueueMetricEvents
            .Where(e => e.DecisionStatus != null)
            .GroupBy(e => e.DecisionStatus!)
            .Select(g => new DecisionMixEntry(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new ObservabilitySnapshot(depthSnapshots, recent, decisionMix, now);
    }

    private static void GuardRange(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc)
            throw new ArgumentException("The 'to' time must be after the 'from' time.", nameof(toUtc));

        if (toUtc - fromUtc > MaxRange)
            throw new ArgumentException(
                $"The requested range exceeds the maximum of {MaxRange.TotalDays} days.", nameof(toUtc));
    }

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

    // Maps a ThroughputGranularity to its bucket SQL and the generate_series step. This is the
    // ONLY place a granularity becomes SQL — an out-of-range enum value throws here, never
    // reaching the query.
    private readonly record struct BucketSpec(string Unit, int? FloorMinutes, TimeSpan Interval)
    {
        public static BucketSpec For(ThroughputGranularity granularity) => granularity switch
        {
            ThroughputGranularity.Second => new("second", null, TimeSpan.FromSeconds(1)),
            ThroughputGranularity.Minute => new("minute", null, TimeSpan.FromMinutes(1)),
            ThroughputGranularity.FiveMinute => new("minute", 5, TimeSpan.FromMinutes(5)),
            ThroughputGranularity.TenMinute => new("minute", 10, TimeSpan.FromMinutes(10)),
            ThroughputGranularity.Hour => new("hour", null, TimeSpan.FromHours(1)),
            ThroughputGranularity.Day => new("day", null, TimeSpan.FromDays(1)),
            _ => throw new ArgumentException(
                $"Unsupported throughput granularity '{granularity}'.", nameof(granularity)),
        };

        // The SQL expression that produces the bucket key for a timestamp column. For the
        // calendar granularities this is a plain date_trunc; for the derived 5/10-minute
        // buckets it floors the unix epoch to the nearest multiple and converts back, so the
        // boundaries align to wall-clock 0/5/10... minute marks. The multiplier comes from the
        // server-side enum mapping, never from caller input.
        public string BucketExpression(string column) =>
            FloorMinutes is { } minutes
                ? $"to_timestamp(floor(extract(epoch from {column}) / {minutes * 60}) * {minutes * 60})"
                : $"date_trunc('{Unit}', {column})";
    }
}
