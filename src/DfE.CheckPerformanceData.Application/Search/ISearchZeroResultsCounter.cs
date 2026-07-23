namespace DfE.CheckPerformanceData.Application.Search;

// In-process, DI-singleton counter of "search returned zero hits" events. Increment is
// called by the telemetry sink whenever a request produces an empty hit set; Read is the
// out-of-band diagnostic accessor used by unit tests and any future admin surface that
// wants to expose the running total. The implementation is Interlocked-backed so
// concurrent search requests can Increment safely without lock contention. Not a metrics
// export target — a proper Meter/OpenTelemetry surface would be a separate seam.
public interface ISearchZeroResultsCounter
{
    void Increment();
    long Read();
}
