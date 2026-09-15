using DfE.CheckPerformanceData.Application.Analytics;

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

    // A typeahead reports once the person has settled on a query, not once per keystroke, and
    // it reports from the browser — which is the only place that knows what was actually shown
    // and whether any of it was taken. Separate from RecordSearch because the event shape is
    // different and because the two must stay countable apart on the dashboard.
    void RecordInstantSearch(InstantSearchTelemetryEvent evt);
}
