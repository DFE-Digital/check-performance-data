namespace DfE.CheckPerformanceData.Application.Search;

// In-process, DI-Singleton counter of "search returned zero hits" events. Backed by an
// instance long incremented via Interlocked.Increment and read back via Interlocked.Read so
// concurrent search requests can bump the counter without lock contention and readers see
// a full memory-barrier view of the running total. The counter is not persisted, is not
// exported to any external metrics backend, and does not survive a process restart — it is
// a diagnostic-only accessor for out-of-band inspection (unit tests, any future admin
// surface). No Reset method is provided by design: the process lifetime IS the reset seam.
public sealed class SearchZeroResultsCounter : ISearchZeroResultsCounter
{
    private long _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public long Read() => Interlocked.Read(ref _count);
}
