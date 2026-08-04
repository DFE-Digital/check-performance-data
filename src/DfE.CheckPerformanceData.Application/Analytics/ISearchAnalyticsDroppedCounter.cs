namespace DfE.CheckPerformanceData.Application.Analytics;

// In-process singleton counter of "search analytics event dropped" occurrences — bumped
// when the bounded analytics channel refuses a write (Channel.Writer.TryWrite returns
// false). Mirrors ISearchZeroResultsCounter's diagnostic contract: not persisted, not
// exported to any external metrics backend, process-lifetime scope. The counter exists so
// integration tests + future admin surfaces can see whether events were shed under load
// without needing a full log-pipeline reach.
public interface ISearchAnalyticsDroppedCounter
{
    void Increment();
    long Read();
}
