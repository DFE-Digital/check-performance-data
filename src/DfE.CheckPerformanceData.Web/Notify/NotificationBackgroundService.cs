using DfE.CheckPerformanceData.Application.Notify;

namespace DfE.CheckPerformanceData.Web.Notify;

/// <summary>
/// Drains the in-process notification channel and sends each email off the request thread.
/// For every message it opens a fresh DI scope (the sender and its DfE Sign-in client are
/// scoped) and delegates to <see cref="INotificationSender"/>. A failure sending one message
/// is logged and swallowed so the pump keeps running. This worker carries no business logic —
/// it only pumps and delegates.
/// </summary>
public sealed class NotificationBackgroundService(
    ChannelNotificationDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notification in dispatcher.Reader.ReadAllAsync(stoppingToken))
            {
                await SendAsync(notification, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested — stop draining.
        }
    }

    private async Task SendAsync(EmailNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
            await sender.SendAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send {NotificationType} notification for ref {ReferenceNumber}",
                notification.Type, notification.ReferenceNumber);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new messages and let the loop drain what remains within the host's
        // shutdown timeout. Anything still queued on an ungraceful kill is lost — acceptable
        // until the durable queue migration.
        dispatcher.Complete();
        await base.StopAsync(cancellationToken);
    }
}
