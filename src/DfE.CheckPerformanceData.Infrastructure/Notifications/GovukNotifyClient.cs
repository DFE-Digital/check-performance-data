using DfE.CheckPerformanceData.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notify.Client;
using Notify.Interfaces;

namespace DfE.CheckPerformanceData.Infrastructure.Notifications;

// Sends email through GOV.UK Notify. The API key arrives via NotifySettings (Key Vault /
// configuration) and is never logged or interpolated into any message. When no key is
// configured the client has no underlying NotificationClient and degrades to a structured
// warning instead of throwing, so the worker keeps running until Notify is provisioned.
public sealed class GovukNotifyClient : INotifyClient
{
    private readonly IAsyncNotificationClient? _client;
    private readonly string? _templateId;
    private readonly ILogger<GovukNotifyClient> _logger;

    public GovukNotifyClient(IOptions<NotifySettings> settings, ILogger<GovukNotifyClient> logger)
    {
        var value = settings.Value;
        _client = string.IsNullOrWhiteSpace(value.ApiKey) ? null : new NotificationClient(value.ApiKey);
        _templateId = value.DlqAlertTemplateId;
        _logger = logger;
    }

    // Direct-collaborator constructor so the underlying client and template can be supplied
    // explicitly; a null client means no API key was configured and sends degrade to a warning.
    public GovukNotifyClient(IAsyncNotificationClient? notificationClient, string? templateId, ILogger<GovukNotifyClient> logger)
    {
        _client = notificationClient;
        _templateId = templateId;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string recipient,
        IReadOnlyDictionary<string, object> personalisation,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(_templateId))
        {
            _logger.LogWarning(
                "GOV.UK Notify is not configured; skipping email to a configured recipient. Configure the Notify API key and template to enable alerting.");
            return;
        }

        // Honour cancellation before issuing the network call so a worker shutdown mid-alert
        // does not start a send it cannot observe.
        cancellationToken.ThrowIfCancellationRequested();

        var notifyPersonalisation = personalisation.ToDictionary(
            kvp => kvp.Key,
            kvp => (dynamic)kvp.Value);

        // The async Notify API returns a Task we actually await, so the send no longer blocks the
        // worker loop the way the synchronous SendEmail did.
        await _client.SendEmailAsync(recipient, _templateId, notifyPersonalisation);
    }
}
