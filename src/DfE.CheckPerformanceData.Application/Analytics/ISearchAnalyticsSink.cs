namespace DfE.CheckPerformanceData.Application.Analytics;

// Write-side abstraction over the search-events history. The Postgres implementation
// batches rows so a request never awaits the sink; an alternative implementation could
// forward to App Insights or another telemetry backend without changing the emitter.
// This surface is EVENTS-ONLY — messages have their own owner (ISearchMessageService).
// Do NOT add a messages-purge method here: the retention job resolves both interfaces
// separately, and two delete paths to the same table would let the two owners drift.
public interface ISearchAnalyticsSink
{
    Task RecordBatchAsync(IReadOnlyList<SearchEventDto> events, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}
