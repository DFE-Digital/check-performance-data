using System.Threading.Channels;
using DfE.CheckPerformanceData.Application.Notify;

namespace DfE.CheckPerformanceData.Web.Notify;

/// <summary>
/// In-process <see cref="INotificationDispatcher"/> backed by an unbounded
/// <see cref="Channel{T}"/>. Producers (request threads) write without blocking; the single
/// <see cref="NotificationBackgroundService"/> consumer drains <see cref="Reader"/>.
/// Registered as a singleton so producers and the worker share one channel. Swapping this for
/// a durable queue-backed implementation is the intended future migration.
/// </summary>
public sealed class ChannelNotificationDispatcher(ILogger<ChannelNotificationDispatcher> logger)
    : INotificationDispatcher
{
    // Unbounded so enqueueing never blocks the request thread. Volume is low (a handful of
    // human-paced emails per submission); durability/backpressure come with the future queue.
    private readonly Channel<EmailNotification> _channel = Channel.CreateUnbounded<EmailNotification>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>The reader drained by the background worker.</summary>
    public ChannelReader<EmailNotification> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(EmailNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_channel.Writer.TryWrite(notification))
        {
            // Only happens if the channel has been completed (shutting down). The email is
            // dropped rather than blocking the request; log so it is visible.
            logger.LogError(
                "Failed to enqueue {NotificationType} notification for ref {ReferenceNumber}; channel is closed",
                notification.Type, notification.ReferenceNumber);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Complete the writer so a draining reader finishes once the channel empties.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
