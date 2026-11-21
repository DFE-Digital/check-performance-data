using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Reports;

// The media reports read the library and its content through the store, and the resized copies
// through one listing of the blob store. Nothing is written.
public sealed class MediaReportServiceTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private readonly IMediaReportStore _store = Substitute.For<IMediaReportStore>();
    private readonly IMediaBlobStore _blobs = Substitute.For<IMediaBlobStore>();
    private readonly MediaReportService _sut;

    public MediaReportServiceTests()
    {
        _sut = new MediaReportService(_store, _blobs, new FakeTimeProvider(new DateTimeOffset(Now)));
        _store.ListAssetsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.ListCurrentPageVersionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _store.ListContentBlocksAsync(Arg.Any<CancellationToken>()).Returns([]);
        _blobs.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task TheAltTextReport_ChecksTheLibrary_AndTheCurrentContentAtThisMoment()
    {
        _store.ListAssetsAsync(Arg.Any<CancellationToken>()).Returns([new MediaReportAsset(A, "a.png", "image", null, Now)]);
        _store.ListContentBlocksAsync(Arg.Any<CancellationToken>())
            .Returns([new MediaReportBlock(1, "footer", Now, $"<img data-media-id=\"{A}\">")]);

        var report = await _sut.GetAltTextReportAsync();

        Assert.Equal(AltTextProblem.Generic, Assert.Single(report.Assets).Problem);
        Assert.True(Assert.Single(report.Uses).FromLibrary);
        await _store.Received(1).ListCurrentPageVersionsAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSizeReport_AddsUpTheResizedCopies_InTotalAndForEachImage()
    {
        var totals = new MediaSizeTotals(2, 3, 3_000, 4_000);
        _store.GetSizeTotalsAsync(Arg.Any<CancellationToken>()).Returns(totals);
        _store.ListLargestAsync(10, Arg.Any<CancellationToken>()).Returns(
        [
            new MediaSizeAsset(A, "a.png", "image/png", 2, 2_000, 2, 3_000),
            new MediaSizeAsset(B, "b.png", "image/png", 1, 1_000, 1, 1_000),
        ]);
        _blobs.ListAsync(MediaBlobKeys.AllRenditionsPrefix, Arg.Any<CancellationToken>()).Returns(
        [
            new MediaBlobEntry(MediaBlobKeys.Rendition(A, 1, "abc"), 100),
            new MediaBlobEntry(MediaBlobKeys.Rendition(A, 2, "def"), 200),
            new MediaBlobEntry(MediaBlobKeys.Rendition(Guid.NewGuid(), 1, "abc"), 50),
            new MediaBlobEntry("renditions/stray-file", 7),
        ]);

        var report = await _sut.GetSizeReportAsync(10);

        Assert.Same(totals, report.Totals);
        Assert.Equal(new MediaRenditionTally(4, 357), report.Renditions);
        Assert.Equal([A, B], report.Largest.Select(r => r.Asset.Id));
        Assert.Equal(new MediaRenditionTally(2, 300), report.Largest[0].Renditions);
        Assert.Equal(MediaRenditionTally.None, report.Largest[1].Renditions);
    }
}
