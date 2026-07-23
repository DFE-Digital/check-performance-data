namespace DfE.CheckPerformanceData.Application.Search;

// Fire-and-forget emission surface for a completed search request. Callers assemble a
// single SearchTelemetryEvent covering the whole request (summary metrics + per-hit rank
// breakdown + per-exclusion breadcrumbs) and hand it off exactly once. The interface is
// deliberately synchronous and single-method so log-sink and future DB-sink implementations
// can share the same surface — a log sink writes structured lines, a DB sink writes a
// 1:N row pair; neither shape leaks into the caller.
public interface ISearchTelemetry
{
    void RecordSearch(SearchTelemetryEvent evt);
}
