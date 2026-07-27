using System.Threading.Channels;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Contract tests for SinkAndLogSearchTelemetry — the composite ISearchTelemetry decorator
// that fans one RecordSearch call into (a) an enqueue onto the bounded analytics channel
// and (b) a delegate to the inner LoggerSearchTelemetry. Four load-bearing facts:
//
//   * Happy path with a live HttpContext + session: the mapped SearchEventDto lands on the
//     channel reader AND the inner logger writes its summary log line. The dto carries the
//     Session.Id verbatim — server-side render only, never a cookie read.
//   * Channel full: TryWrite returns false. The decorator MUST still call the inner logger
//     AND MUST increment the drop counter. Warn line fires on the decorator's own logger
//     (not the inner's) so ops can see the shed under load.
//   * No HttpContext (background-path caller, or a future non-request thread): decorator
//     skips the channel write entirely (nothing to attribute) but still calls the inner
//     logger — an unattributable event is still worth logging.
//   * Constructor shape: takes the CONCRETE LoggerSearchTelemetry, not the interface. Web
//     DI registers LoggerSearchTelemetry as a distinct Scoped so the decorator resolves
//     cleanly without a self-recursive ISearchTelemetry lookup.
public sealed class SinkAndLogSearchTelemetryTests
{
    // Small no-op debug-options fake so the inner LoggerSearchTelemetry constructs
    // without needing the CMS settings store on the classpath.
    private sealed class FakeSearchDebugOptions : ISearchDebugOptions
    {
        public bool ShowSearchDebug => false;
    }

    // Minimal session-provider fake — swaps between "live session id" and "no session"
    // without dragging in the AspNetCore Http surface. The abstraction is the seam the
    // decorator was built around exactly so unit tests can construct one line.
    private sealed class FakeSessionProvider(string? id) : ISearchAnalyticsSessionProvider
    {
        public string? GetSessionId() => id;
    }

    private static SearchTelemetryEvent BuildEvent(int hitCount = 1) =>
        new(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: DateTime.UtcNow,
            QueryRaw: "widget",
            QueryNormalised: "widget",
            Scope: null,
            LatencyMsTotal: 42L,
            LatencyMsPages: 25L,
            LatencyMsBlocks: 17L,
            Hits: Enumerable.Range(0, hitCount)
                .Select(i => new SearchHitEvent(
                    Url: $"/url-{i}", Title: $"title-{i}", RankTotal: 0.1f * (i + 1),
                    PageContributed: true, BlockContributorCount: 0,
                    ContributingBlockKeys: [], RankKeywords: null, RankTitle: null,
                    RankSubtitle: null, RankBody: null, RankValue: null))
                .ToList(),
            FilterExclusions: []);

    private static (SinkAndLogSearchTelemetry Sut,
                    Channel<SearchEventDto> Channel,
                    ISearchAnalyticsDroppedCounter Counter,
                    ILogger<SinkAndLogSearchTelemetry> DecoratorLogger,
                    ILogger<LoggerSearchTelemetry> InnerLogger)
        BuildSut(string? sessionId, int channelCapacity = 100)
    {
        // Match the production channel's Wait mode so TryWrite returns false on overflow
        // (DropWrite silently accepts + discards, hiding the drop from the decorator).
        var channel = Channel.CreateBounded<SearchEventDto>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        var innerLogger = Substitute.For<ILogger<LoggerSearchTelemetry>>();
        var inner = new LoggerSearchTelemetry(
            innerLogger, new SearchZeroResultsCounter(), new FakeSearchDebugOptions());

        var counter = Substitute.For<ISearchAnalyticsDroppedCounter>();
        var decoratorLogger = Substitute.For<ILogger<SinkAndLogSearchTelemetry>>();

        var sut = new SinkAndLogSearchTelemetry(
            channel.Writer, inner, counter, decoratorLogger, new FakeSessionProvider(sessionId));

        return (sut, channel, counter, decoratorLogger, innerLogger);
    }

    [Fact]
    public async Task RecordSearch_WithLiveSession_EnqueuesDtoWithSessionIdAndCallsInnerLogger()
    {
        var (sut, channel, counter, _, innerLogger) = BuildSut("session-abc");
        var evt = BuildEvent(hitCount: 2);

        sut.RecordSearch(evt);

        // The mapped dto lands on the reader.
        Assert.True(channel.Reader.TryRead(out var dto));
        Assert.Equal("session-abc", dto!.SessionId);
        Assert.Equal(evt.UtcTimestamp, dto.OccurredAtUtc);
        Assert.Equal(evt.QueryRaw, dto.QueryRaw);
        Assert.Equal(2, dto.Results.Count);

        // No further items on the reader (single enqueue per RecordSearch).
        Assert.False(channel.Reader.TryRead(out _));

        // Inner logger got its Info summary line — pin via the summary template shape rather
        // than the exact literal so a placeholder rename does not falsely trip.
        innerLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Search completed") &&
                                o.ToString()!.Contains("SearchId")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Drop counter did NOT move on the happy path.
        counter.DidNotReceive().Increment();

        await Task.CompletedTask;
    }

    [Fact]
    public void RecordSearch_ChannelFull_IncrementsDroppedCounterAndStillCallsInnerLogger()
    {
        // Capacity 1 + DropWrite: fill the channel first, so the decorator's TryWrite
        // returns false on the RecordSearch under test.
        var (sut, channel, counter, decoratorLogger, innerLogger) =
            BuildSut("session-xyz", channelCapacity: 1);

        // Prime the channel — one already-queued item is enough to make DropWrite refuse
        // the next TryWrite until the reader drains one.
        Assert.True(channel.Writer.TryWrite(new SearchEventDto(
            OccurredAtUtc: DateTime.UtcNow,
            SessionId: "seed",
            QueryRaw: "seed",
            QueryNormalised: "seed",
            Scope: null,
            ResultsPages: 0,
            ResultsBlocks: 0,
            LatencyMs: 0,
            Results: [])));

        var evt = BuildEvent();

        sut.RecordSearch(evt);

        // Counter moved — the decorator recognised the failed enqueue.
        counter.Received(1).Increment();

        // Inner logger still fired (delegation to log is orthogonal to sink success).
        innerLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Search completed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Decorator's own logger fires the drop warning (not the inner's).
        decoratorLogger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("dropped") &&
                                o.ToString()!.Contains("SearchId")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void RecordSearch_WithNoSessionAvailable_SkipsChannelWriteButStillCallsInnerLogger()
    {
        var (sut, channel, counter, _, innerLogger) = BuildSut(sessionId: null);
        var evt = BuildEvent();

        sut.RecordSearch(evt);

        // Nothing enqueued — the decorator has no session to attribute.
        Assert.False(channel.Reader.TryRead(out _));

        // No drop counter movement — a skipped-by-design write is not a drop.
        counter.DidNotReceive().Increment();

        // Inner logger still fired.
        innerLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Search completed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
