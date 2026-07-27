namespace DfE.CheckPerformanceData.Application.Analytics;

// In-process, DI-Singleton counter of "search analytics event dropped" occurrences.
// Backed by an instance long, bumped and read through atomic interlocked primitives so
// concurrent /search requests can move the value without lock contention and readers see
// a full memory-barrier view of the running total. The counter is not persisted, is not
// exported to any external metrics backend, and does not survive a process restart — it
// is a diagnostic-only accessor for out-of-band inspection (integration tests, any future
// admin surface). No zero-out method is provided by design: the process lifetime IS the
// reset seam.
public sealed class SearchAnalyticsDroppedCounter : ISearchAnalyticsDroppedCounter
{
    private long _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public long Read() => Interlocked.Read(ref _count);
}
