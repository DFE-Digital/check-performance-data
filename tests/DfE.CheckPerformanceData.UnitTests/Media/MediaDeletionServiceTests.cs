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
