using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using MetricDto = DfE.CheckPerformanceData.Application.Observability.QueueMetricEvent;
using MetricEntity = DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent;

namespace DfE.CheckPerformanceData.Persistence.Observability;

// Postgres implementation of the metrics sink: each record becomes one inserted row, and the
// purge deletes rows whose recorded_at_utc has aged past the retention window. The insert is
// deliberately a plain add/save so the caller can run it outside the dequeue-ack transaction —
// a sink failure here must never roll back message processing.
public sealed class DbMetricsSink : IMetricsSink
{
    private readonly IPortalDbContext _dbContext;

    public DbMetricsSink(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RecordAsync(MetricDto metric, CancellationToken cancellationToken)
    {
        _dbContext.QueueMetricEvents.Add(new MetricEntity
        {
            QueueName = metric.QueueName,
            Stage = metric.Stage,
            ReferenceNumber = metric.ReferenceNumber,
            MessageId = metric.MessageId,
            DecisionStatus = metric.DecisionStatus,
            RulesVersion = metric.RulesVersion,
            LatencyMs = metric.LatencyMs,
            RecordedAtUtc = metric.RecordedAtUtc,
            StartedAtUtc = metric.StartedAtUtc,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordManyAsync(IEnumerable<MetricDto> metrics, CancellationToken cancellationToken)
    {
        // One AddRange + one SaveChanges so seeding a couple of months of history is a single round
        // trip rather than thousands. Like RecordAsync this is a plain insert outside any dequeue-ack
        // transaction — it only ever runs from the dev-only seed tool against a development database.
        _dbContext.QueueMetricEvents.AddRange(metrics.Select(metric => new MetricEntity
        {
            QueueName = metric.QueueName,
            Stage = metric.Stage,
            ReferenceNumber = metric.ReferenceNumber,
            MessageId = metric.MessageId,
            DecisionStatus = metric.DecisionStatus,
            RulesVersion = metric.RulesVersion,
            LatencyMs = metric.LatencyMs,
            RecordedAtUtc = metric.RecordedAtUtc,
            StartedAtUtc = metric.StartedAtUtc,
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        return await _dbContext.QueueMetricEvents
            .Where(e => e.RecordedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
