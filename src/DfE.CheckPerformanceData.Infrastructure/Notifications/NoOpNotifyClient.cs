using DfE.CheckPerformanceData.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.Notifications;

// Placeholder notification client. The GOV.UK Notify integration is delivered under a
// separate ticket; until that lands this no-op stands in so the dead-letter alerting
// pipeline is fully wired and exercised end to end. Each call logs a structured warning
// (operational metadata only, never a recipient or personal identifier) so an unsent
// alert is visible in the logs rather than silently dropped.
public sealed class NoOpNotifyClient : INotifyClient
{
    private readonly ILogger<NoOpNotifyClient> _logger;

    public NoOpNotifyClient(ILogger<NoOpNotifyClient> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(
        string recipient,
        IReadOnlyDictionary<string, object> personalisation,
        CancellationToken cancellationToken = default)
    {
        // Honour cancellation before doing any work so a worker shutdown mid-alert is observed.
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning(
            "Notification suppressed: GOV.UK Notify is not yet wired in (delivered under a separate ticket). " +
            "An alert with {PersonalisationCount} personalisation field(s) would have been sent.",
            personalisation.Count);

        return Task.CompletedTask;
    }
}
