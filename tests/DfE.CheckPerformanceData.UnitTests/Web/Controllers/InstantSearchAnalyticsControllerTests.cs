using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The instant-search analytics endpoint takes a report from the browser, which means every
// value in it is untrusted. These tests pin the guards: the surface has to be one this app
// knows, the query is sliced, the result list is capped, and anything malformed is dropped
// rather than persisted as a half-row.
//
// Method-level [Trait("search-case", ...)] is load-bearing — the coverage meta-test
// enumerates the trait across the search test assemblies.
public sealed class InstantSearchAnalyticsControllerTests
{
    private readonly ISearchTelemetry _telemetry = Substitute.For<ISearchTelemetry>();

    private InstantSearchAnalyticsController CreateSut()
    {
        var http = new DefaultHttpContext();
        http.Session = new StubSession();
        return new InstantSearchAnalyticsController(_telemetry)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static InstantSearchReport Report(
        string surface = SearchSurfaces.InstantPage,
        string q = "evidence",
        string? shown = """[{"position":1,"kind":"section","key":"#providing-evidence","label":"Providing evidence"}]""",
        string? hostPath = "/help/uploading",
        string? selectedKey = null,
        int? selectedPosition = null,
        int latencyMs = 12) =>
        new()
        {
            Surface = surface,
            Q = q,
            Scope = null,
            HostPath = hostPath,
            Shown = shown,
            SelectedKey = selectedKey,
            SelectedPosition = selectedPosition,
            LatencyMs = latencyMs,
        };

    [Fact]
    public async Task AWellFormedReport_IsRecorded()
    {
        var sut = CreateSut();

        var result = await sut.Record(Report(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _telemetry.Received(1).RecordInstantSearch(Arg.Is<InstantSearchTelemetryEvent>(e =>
            e.Surface == SearchSurfaces.InstantPage
            && e.QueryRaw == "evidence"
            && e.HostPath == "/help/uploading"
            && e.Shown.Count == 1));
    }

    [Fact]
    public async Task AnUnknownSurface_IsRejected()
    {
        var sut = CreateSut();

        var result = await sut.Record(Report(surface: "made-up"), CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        _telemetry.DidNotReceive().RecordInstantSearch(Arg.Any<InstantSearchTelemetryEvent>());
    }

    [Fact]
    [Trait("search-case", "very-short")]
    public async Task AQueryBelowTheMinimum_IsDropped()
    {
        var sut = CreateSut();

        await sut.Record(Report(q: "a"), CancellationToken.None);

        _telemetry.DidNotReceive().RecordInstantSearch(Arg.Any<InstantSearchTelemetryEvent>());
    }

    [Fact]
    [Trait("search-case", "long-query")]
    public async Task ALongQuery_IsSlicedToOneHundredCharacters()
    {
        var sut = CreateSut();

        await sut.Record(Report(q: new string('a', 500)), CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(
            Arg.Is<InstantSearchTelemetryEvent>(e => e.QueryRaw.Length == 100));
    }

    [Fact]
    public async Task TheShownListIsCapped_SoAReportCannotWriteUnboundedRows()
    {
        var many = string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $$"""{"position":{{i}},"kind":"section","key":"#s{{i}}","label":"S{{i}}"}"""));
        var sut = CreateSut();

        await sut.Record(Report(shown: $"[{many}]"), CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(
            Arg.Is<InstantSearchTelemetryEvent>(e => e.Shown.Count == 10));
    }

    [Fact]
    public async Task AnUnknownResultKind_FallsBackToPage_RatherThanBeingTrusted()
    {
        var sut = CreateSut();

        await sut.Record(
            Report(shown: """[{"position":1,"kind":"javascript:","key":"/x","label":"X"}]"""),
            CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(Arg.Is<InstantSearchTelemetryEvent>(e =>
            e.Shown.Count == 1 && e.Shown[0].Kind == "page"));
    }

    [Fact]
    public async Task MalformedShownJson_DropsTheReport_RatherThanWritingAHalfRow()
    {
        var sut = CreateSut();

        var result = await sut.Record(Report(shown: "not json at all"), CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        _telemetry.DidNotReceive().RecordInstantSearch(Arg.Any<InstantSearchTelemetryEvent>());
    }

    [Fact]
    public async Task AnEmptyMenu_IsStillRecorded()
    {
        // A query that showed nothing is the zero-result signal; dropping it would make the
        // instant surface look like it always finds something.
        var sut = CreateSut();

        await sut.Record(Report(shown: "[]"), CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(
            Arg.Is<InstantSearchTelemetryEvent>(e => e.Shown.Count == 0));
    }

    [Fact]
    public async Task ASelection_IsCarriedThroughWithItsPosition()
    {
        var sut = CreateSut();

        await sut.Record(
            Report(selectedKey: "#providing-evidence", selectedPosition: 1),
            CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(Arg.Is<InstantSearchTelemetryEvent>(e =>
            e.SelectedKey == "#providing-evidence" && e.SelectedPosition == 1));
    }

    [Fact]
    public async Task ASelectionPositionOutsideTheMenu_IsDiscarded()
    {
        var sut = CreateSut();

        await sut.Record(
            Report(selectedKey: "#x", selectedPosition: 999), CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(
            Arg.Is<InstantSearchTelemetryEvent>(e => e.SelectedPosition == null));
    }

    [Fact]
    public async Task AHostPathIsOnlyKept_ForAnOnPageSearch()
    {
        var sut = CreateSut();

        await sut.Record(
            Report(surface: SearchSurfaces.Instant, hostPath: "/help/uploading"),
            CancellationToken.None);

        _telemetry.Received(1).RecordInstantSearch(
            Arg.Is<InstantSearchTelemetryEvent>(e => e.HostPath == null));
    }

    // Minimal ISession so the controller can establish a session identity without a real
    // session store; DefaultHttpContext does not supply one.
    private sealed class StubSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id => "stub-session";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
