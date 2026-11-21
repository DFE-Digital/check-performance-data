using DfE.CheckPerformanceData.Application.Media;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media;

public sealed class MediaDeletionServiceTests
{
    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaBlobStore _blobs = Substitute.For<IMediaBlobStore>();
    private readonly CapturingLogger _logger = new();
    private readonly MediaDeletionService _sut;
    private readonly Guid _id = Guid.NewGuid();

    public MediaDeletionServiceTests()
    {
        _sut = new MediaDeletionService(_assets, _blobs, _logger);
    }

    // The rows go first: a row must never point at a missing file, but a file with no row is
    // harmless (nothing reads it).
    [Fact]
    public async Task Delete_RemovesTheRows_ThenEveryFileUnderTheImagesPrefixes()
    {
        var calls = new List<string>();
        _assets.DeleteAsync(_id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("rows");
            return MediaDeleteOutcome.Deleted;
        });
        _blobs.DeleteByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            calls.Add(ci.Arg<string>());
            return 1;
        });

        Assert.Equal(MediaDeleteOutcome.Deleted, await _sut.DeleteAsync(_id));

        Assert.Equal(["rows", .. MediaBlobKeys.AssetPrefixes(_id)], calls);
    }

    [Theory]
    [InlineData(MediaDeleteOutcome.InUse)]
    [InlineData(MediaDeleteOutcome.NotFound)]
    public async Task Delete_LeavesTheFiles_WhenTheRowsAreNotDeleted(MediaDeleteOutcome outcome)
    {
        _assets.DeleteAsync(_id, Arg.Any<CancellationToken>()).Returns(outcome);

        Assert.Equal(outcome, await _sut.DeleteAsync(_id));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteByPrefixAsync(default!, default);
    }

    // The image is gone once its rows are; a storage failure must not turn that into an error page.
    [Fact]
    public async Task Delete_LogsAFileFailure_AndStillTriesTheOtherPrefix()
    {
        var prefixes = MediaBlobKeys.AssetPrefixes(_id);
        _assets.DeleteAsync(_id, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.Deleted);
        _blobs.DeleteByPrefixAsync(prefixes[0], Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("storage down"));

        Assert.Equal(MediaDeleteOutcome.Deleted, await _sut.DeleteAsync(_id));

        await _blobs.Received(1).DeleteByPrefixAsync(prefixes[1], Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(prefixes[0]));
    }

    // "Delete unused images": each image goes through the same delete, which checks its uses
    // again, so an image that content started to use after the confirmation page stays.
    [Fact]
    public async Task DeleteUnused_DeletesEachImage_SkipsOnesNowInUse_AndRecordsOneAuditRow()
    {
        var gone = Guid.NewGuid();
        var used = Guid.NewGuid();
        var missing = Guid.NewGuid();
        _assets.DeleteAsync(gone, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.Deleted);
        _assets.DeleteAsync(used, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.InUse);
        _assets.DeleteAsync(missing, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.NotFound);

        var result = await _sut.DeleteUnusedAsync([gone, used, missing, gone]);

        Assert.Equal([gone], result.Deleted);
        Assert.Equal([used], result.InUse);
        Assert.Equal([missing], result.NotFound);
        Assert.NotEqual(Guid.Empty, result.RunId);
        await _assets.Received(1).DeleteAsync(gone, Arg.Any<CancellationToken>());
        await _assets.Received(1).RecordUnusedDeleteAsync(result, Arg.Any<CancellationToken>());
        await _blobs.Received(2).DeleteByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUnused_TakesAtMostOneRunOfImages()
    {
        _assets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.Deleted);
        var ids = Enumerable.Range(0, MediaDeletionService.MaxUnusedPerRun + 5).Select(_ => Guid.NewGuid()).ToList();

        var result = await _sut.DeleteUnusedAsync(ids);

        Assert.Equal(MediaDeletionService.MaxUnusedPerRun, result.Deleted.Count);
        await _assets.DidNotReceive().DeleteAsync(ids[^1], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUnused_WithNoImages_DoesNothing()
    {
        var result = await _sut.DeleteUnusedAsync([]);

        Assert.Empty(result.Deleted);
        await _assets.DidNotReceiveWithAnyArgs().RecordUnusedDeleteAsync(default!, default);
    }

    private sealed class CapturingLogger : ILogger<MediaDeletionService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
