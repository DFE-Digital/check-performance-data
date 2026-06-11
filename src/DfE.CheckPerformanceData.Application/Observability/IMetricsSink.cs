namespace DfE.CheckPerformanceData.Application.Observability;

// Write-side abstraction over the metrics events history. The Postgres implementation
// inserts a row; an alternative implementation could forward to App Insights or another
// telemetry backend without changing the consumers. PurgeExpiredAsync bounds the table's
// growth on the retention schedule; keeping both write and purge on one type means there
// is a single owner of the events store.
public interface IMetricsSink
{
    Task RecordAsync(QueueMetricEvent metric, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}
