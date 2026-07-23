using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search.TestDoubles;

// Hand-rolled ISearchTelemetry capturing every event fed to RecordSearch, in call order.
// Preferred over NSubstitute for these tests because field-level assertions on a
// SearchTelemetryEvent read cleaner as `sut.LastEvent.QueryNormalised` than as a chain of
// Arg.Is<...>() matchers — and the event carries two nested collections whose full-field
// assertions the substitute syntax would obscure. LastEvent is a convenience for the
// single-emission case (the whole codebase's search flow emits exactly one event per
// request) — inspect Events directly when a test drives multiple RecordSearch calls.
public sealed class FakeSearchTelemetry : ISearchTelemetry
{
    public List<SearchTelemetryEvent> Events { get; } = [];

    public SearchTelemetryEvent LastEvent => Events[^1];

    public void RecordSearch(SearchTelemetryEvent evt) => Events.Add(evt);
}
