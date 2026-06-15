using Dfe.Analytics.Events;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Infrastructure.Analytics;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure.Analytics;

public sealed class DfeAnalyticsServiceTests
{
    /// <summary>A test event exercising plain, hidden, bool and null fields.</summary>
    private sealed record SampleEvent : AnalyticsEvent
    {
        public override string EventType => "sample_event";
        public override IReadOnlyList<AnalyticsField> Fields =>
        [
            new("plain", "value"),
            new("flag", true),
            new("count", 3),
            new("secret", "abc", Hidden: true),
            new("absent", null),
        ];
    }

    private static (DfeAnalyticsService Sut, IEventSender Sender, Func<Event?> Sent) NewSut()
    {
        var factory = Substitute.For<IEventFactory>();
        factory.CreateEvent(Arg.Any<string>())
            .Returns(ci => new Event { EventType = ci.Arg<string>(), Environment = "test" });
        var sender = Substitute.For<IEventSender>();
        sender.EventFactory.Returns(factory);
        Event? sent = null;
        sender.When(s => s.SendEventAsync(Arg.Any<Event>())).Do(ci => sent = ci.Arg<Event>());
        return (new DfeAnalyticsService(sender), sender, () => sent);
    }

    [Fact]
    public async Task TrackAsync_creates_event_with_the_event_type_and_sends_it()
    {
        var (sut, sender, sent) = NewSut();

        await sut.TrackAsync(new SampleEvent());

        await sender.Received(1).SendEventAsync(Arg.Any<Event>());
        Assert.Equal("sample_event", sent()!.EventType);
    }

    [Fact]
    public async Task TrackAsync_maps_plain_fields_as_stringified_data()
    {
        var (sut, _, sent) = NewSut();

        await sut.TrackAsync(new SampleEvent());

        Assert.Equal(["value"], sent()!.Data["plain"]);
        Assert.Equal(["true"], sent()!.Data["flag"]);   // bool → lowercase
        Assert.Equal(["3"], sent()!.Data["count"]);     // number → invariant string
    }

    [Fact]
    public async Task TrackAsync_routes_hidden_fields_to_hidden_data()
    {
        var (sut, _, sent) = NewSut();

        await sut.TrackAsync(new SampleEvent());

        Assert.Equal(["abc"], sent()!.HiddenData["secret"]);
        Assert.DoesNotContain("secret", sent()!.Data.Keys);
    }

    [Fact]
    public async Task TrackAsync_skips_null_valued_fields()
    {
        var (sut, _, sent) = NewSut();

        await sut.TrackAsync(new SampleEvent());

        Assert.DoesNotContain("absent", sent()!.Data.Keys);
        Assert.DoesNotContain("absent", sent()!.HiddenData.Keys);
    }
}
