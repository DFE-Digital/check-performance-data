using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Embedded;

// A run that is stopped (for example when the request is cancelled) finishes the page it is on,
// starts no other, and still writes the run's audit entry with what it did.
public sealed class EmbeddedImageMoverTests
{
    private readonly IEmbeddedImageStore _store = Substitute.For<IEmbeddedImageStore>();

    private EmbeddedImageMover Mover() => new(
        _store, Substitute.For<IMediaUploadService>(), Substitute.For<IMediaAssetRepository>(),
        Substitute.For<IMediaDeletionService>(), Substitute.For<IMediaUrlBuilder>(),
        Substitute.For<ICurrentUserService>(), TimeProvider.System, NullLogger<EmbeddedImageMover>.Instance);

    [Fact]
    public async Task AStoppedRun_StartsNoFurtherPage_AndStillRecordsTheRun()
    {
        using var stop = new CancellationTokenSource();
        var first = new EmbeddedImagePage(Guid.NewGuid(), "First", "first", "content");
        var second = new EmbeddedImagePage(Guid.NewGuid(), "Second", "second", "content");
        _store.ListPagesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([first, second]);
        _store.GetCurrentVersionsAsync(first.NodeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stop.Cancel();
                return (IReadOnlyList<EmbeddedImageVersion>)[];
            });

        var result = await Mover().MoveAsync(stop.Token);

        Assert.Equal(0, result.PagesChanged);
        await _store.DidNotReceive().GetCurrentVersionsAsync(second.NodeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _store.Received(1).RecordRunAsync(Arg.Any<Guid>(), result, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
