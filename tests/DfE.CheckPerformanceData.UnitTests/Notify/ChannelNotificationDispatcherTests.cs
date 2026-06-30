using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Web.Notify;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public sealed class ChannelNotificationDispatcherTests
{
    private static EmailNotification Message(string reference) => new()
    {
        Type = NotificationType.SubmissionConfirmed,
        ReferenceNumber = reference,
        Ukprn = "10000000",
        OriginatorEmail = "user@education.gov.uk"
    };

    [Fact]
    public async Task EnqueueAsync_WritesMessageReadableFromReader()
    {
        var sut = new ChannelNotificationDispatcher(NullLogger<ChannelNotificationDispatcher>.Instance);

        await sut.EnqueueAsync(Message("REF001"));

        var read = await sut.Reader.ReadAsync();
        Assert.Equal("REF001", read.ReferenceNumber);
    }

    [Fact]
    public async Task EnqueueAsync_PreservesOrderForMultipleMessages()
    {
        var sut = new ChannelNotificationDispatcher(NullLogger<ChannelNotificationDispatcher>.Instance);

        await sut.EnqueueAsync(Message("REF001"));
        await sut.EnqueueAsync(Message("REF002"));

        Assert.Equal("REF001", (await sut.Reader.ReadAsync()).ReferenceNumber);
        Assert.Equal("REF002", (await sut.Reader.ReadAsync()).ReferenceNumber);
    }

    [Fact]
    public async Task Complete_CompletesTheReaderSoDrainingFinishes()
    {
        var sut = new ChannelNotificationDispatcher(NullLogger<ChannelNotificationDispatcher>.Instance);
        await sut.EnqueueAsync(Message("REF001"));

        sut.Complete();

        var drained = new List<string>();
        await foreach (var msg in sut.Reader.ReadAllAsync())
        {
            drained.Add(msg.ReferenceNumber);
        }

        Assert.Equal(["REF001"], drained);
    }
}
