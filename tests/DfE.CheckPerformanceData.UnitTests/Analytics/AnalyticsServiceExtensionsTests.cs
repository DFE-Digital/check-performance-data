using DfE.CheckPerformanceData.Application.Analytics;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class AnalyticsServiceExtensionsTests
{
    private sealed record SampleEvent : AnalyticsEvent
    {
        public override string EventType => "sample";
        public override IReadOnlyList<AnalyticsField> Fields => [];
    }

    [Fact]
    public async Task TrackSafeAsync_forwards_to_TrackAsync()
    {
        var analytics = Substitute.For<IAnalyticsService>();
        var ev = new SampleEvent();

        await analytics.TrackSafeAsync(ev);

        await analytics.Received(1).TrackAsync(ev, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrackSafeAsync_swallows_exceptions_so_the_caller_is_never_broken()
    {
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("sink down"));

        // Must not throw — telemetry is best-effort.
        await analytics.TrackSafeAsync(new SampleEvent());
    }
}
