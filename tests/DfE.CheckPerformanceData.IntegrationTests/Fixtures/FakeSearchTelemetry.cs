using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

// Namespace-scoped duplicate of the UnitTests-side FakeSearchTelemetry. The IntegrationTests
// csproj does not project-reference UnitTests (matching the SearchCaseCoverageTests
// filesystem-load precedent), so the fake is duplicated here rather than shared through a
// project reference. Byte-identical implementation aside from the namespace: captures every
// event fed to RecordSearch in call order, exposes LastEvent as a convenience for the
// single-emission case.
public sealed class FakeSearchTelemetry : ISearchTelemetry
{
    public List<SearchTelemetryEvent> Events { get; } = [];

    public SearchTelemetryEvent LastEvent => Events[^1];

    public void RecordSearch(SearchTelemetryEvent evt) => Events.Add(evt);

    // Instant searches are captured on their own list: the two event shapes are different
    // records and a test asserting "exactly one search was recorded" must not be satisfied by
    // a typeahead turning up in the same bucket.
    public List<InstantSearchTelemetryEvent> InstantEvents { get; } = [];

    public void RecordInstantSearch(InstantSearchTelemetryEvent evt) => InstantEvents.Add(evt);
}
