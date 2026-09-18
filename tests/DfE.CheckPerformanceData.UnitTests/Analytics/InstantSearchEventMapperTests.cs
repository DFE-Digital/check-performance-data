using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// An instant search reaches the sink from the browser rather than from SiteSearchService,
// so it has its own mapper. What it must produce is a row the dashboard can read alongside
// a submitted search: same table, same session attribution, with the surface, the host page
// and the selection filled in.
public sealed class InstantSearchEventMapperTests
{
    private static InstantSearchTelemetryEvent PageEvent(
        string? selectedKey = null,
        int? selectedPosition = null,
        params InstantSearchShownHit[] shown) =>
        new(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc),
            QueryRaw: "evidence",
            QueryNormalised: "evidence",
            Scope: null,
            Surface: SearchSurfaces.InstantPage,
            HostPath: "/help/uploading",
            LatencyMs: 12,
            Shown: shown,
            SelectedKey: selectedKey,
            SelectedPosition: selectedPosition);

    [Fact]
    public void OnPageSections_CountAsSections_NotPages()
    {
        // A section is not a document. Counting one as a page would inflate every per-page
        // figure on the dashboard.
        var evt = PageEvent(
            shown: [
                new(1, "section", "#providing-evidence", "Providing evidence"),
                new(2, "section", "#uploading-files", "Uploading files")]);

        var (dto, results) = InstantSearchEventMapper.From(evt, "session-1");

        Assert.Equal(2, dto.ResultsSections);
        Assert.Equal(0, dto.ResultsPages);
        Assert.Equal(0, dto.ResultsBlocks);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("section", r.ResultKind));
    }

    [Fact]
    public void SiteInstantHits_CountAsPages()
    {
        var evt = new InstantSearchTelemetryEvent(
            Guid.NewGuid(), DateTime.UtcNow, "merge", "merge", "guidance",
            SearchSurfaces.Instant, HostPath: null, LatencyMs: 40,
            Shown: [new(1, "page", "/guidance/merging", "Merging pupil records")],
            SelectedKey: null, SelectedPosition: null);

        var (dto, _) = InstantSearchEventMapper.From(evt, "session-1");

        Assert.Equal(1, dto.ResultsPages);
        Assert.Equal(0, dto.ResultsSections);
        Assert.Equal(SearchSurfaces.Instant, dto.Surface);
        Assert.Equal("guidance", dto.Scope);
    }

    [Fact]
    public void TheSurfaceAndHostPage_ReachTheRow()
    {
        var (dto, _) = InstantSearchEventMapper.From(
            PageEvent(shown: new InstantSearchShownHit(1, "section", "#a", "A")), "session-1");

        Assert.Equal(SearchSurfaces.InstantPage, dto.Surface);
        Assert.Equal("/help/uploading", dto.HostPath);
    }

    [Fact]
    public void AChosenResult_IsRecordedWithItsPosition()
    {
        var (dto, _) = InstantSearchEventMapper.From(
            PageEvent(
                selectedKey: "#uploading-files",
                selectedPosition: 2,
                shown: [
                    new(1, "section", "#providing-evidence", "Providing evidence"),
                    new(2, "section", "#uploading-files", "Uploading files")]),
            "session-1");

        Assert.Equal("#uploading-files", dto.SelectedKey);
        Assert.Equal(2, dto.SelectedPosition);
    }

    [Fact]
    public void AMenuShownAndAbandoned_IsRecordedWithNoSelection()
    {
        // The signal a typeahead cannot get any other way: results were offered and none of
        // them were any good, so the person typed something else instead.
        var (dto, results) = InstantSearchEventMapper.From(
            PageEvent(shown: new InstantSearchShownHit(1, "section", "#a", "A")), "session-1");

        Assert.Null(dto.SelectedKey);
        Assert.Null(dto.SelectedPosition);
        Assert.Single(results);
    }

    [Fact]
    public void AQueryThatShowedNothing_IsStillARow()
    {
        // Zero-result instant searches are the whole point of the zero-results dashboard.
        var (dto, results) = InstantSearchEventMapper.From(PageEvent(), "session-1");

        Assert.Empty(results);
        Assert.Equal(0, dto.ResultsSections);
        Assert.Equal("evidence", dto.QueryRaw);
    }

    [Fact]
    public void PositionsComeFromTheOrderShown()
    {
        var (_, results) = InstantSearchEventMapper.From(
            PageEvent(shown: [
                new(1, "section", "#first", "First"),
                new(2, "section", "#second", "Second")]),
            "session-1");

        Assert.Equal([1, 2], results.Select(r => r.Position));
        Assert.Equal(["#first", "#second"], results.Select(r => r.ResultKey));
    }
}
