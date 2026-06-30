using System.Collections.Concurrent;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Web.Notify;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public sealed class NotificationBackgroundServiceTests
{
    private static EmailNotification Message(string reference) => new()
    {
        Type = NotificationType.SubmissionConfirmed,
        ReferenceNumber = reference,
        Ukprn = "10000000",
        OriginatorEmail = "user@education.gov.uk"
    };

    private static (NotificationBackgroundService Service, ChannelNotificationDispatcher Dispatcher) Build(
        INotificationSender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => sender);
        var provider = services.BuildServiceProvider();

        var dispatcher = new ChannelNotificationDispatcher(NullLogger<ChannelNotificationDispatcher>.Instance);
        var service = new NotificationBackgroundService(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationBackgroundService>.Instance);

        return (service, dispatcher);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    [Fact]
    public async Task PumpsEnqueuedMessageThroughToSender()
    {
        var received = new ConcurrentBag<string>();
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(Arg.Any<EmailNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                received.Add(ci.Arg<EmailNotification>().ReferenceNumber);
                return Task.CompletedTask;
            });

        var (service, dispatcher) = Build(sender);
        await service.StartAsync(CancellationToken.None);
        await dispatcher.EnqueueAsync(Message("REF001"));

        await WaitFor(() => received.Contains("REF001"));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ThrowingSenderDoesNotStopProcessingSubsequentMessages()
    {
        var received = new ConcurrentBag<string>();
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(Arg.Any<EmailNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var reference = ci.Arg<EmailNotification>().ReferenceNumber;
                if (reference == "REF001")
                {
                    throw new InvalidOperationException("boom");
                }

                received.Add(reference);
                return Task.CompletedTask;
            });

        var (service, dispatcher) = Build(sender);
        await service.StartAsync(CancellationToken.None);
        await dispatcher.EnqueueAsync(Message("REF001"));
        await dispatcher.EnqueueAsync(Message("REF002"));

        await WaitFor(() => received.Contains("REF002"));
        await service.StopAsync(CancellationToken.None);
    }
}
