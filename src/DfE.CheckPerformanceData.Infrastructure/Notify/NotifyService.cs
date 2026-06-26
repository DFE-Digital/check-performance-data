using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

public class NotifyService : INotifyService
{
    private readonly INotifyEmailClient _client;
    private readonly NotifySettings _settings;
    private readonly ILogger<NotifyService> _logger;
    private readonly ResiliencePipeline? _resiliencePipeline;

    public NotifyService(
        INotifyEmailClient client,
        IOptions<NotifySettings> settings,
        ILogger<NotifyService> logger,
        ResiliencePipeline? resiliencePipeline = null)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _resiliencePipeline = resiliencePipeline;
    }

    public async Task SendNotificationsAsync(
        string referenceNumber,
        string deadline,
        IReadOnlyCollection<string> recipientEmails,
        NotificationType notificationType,
        string? url = null)
    {
        var templateId = notificationType switch
        {
            NotificationType.SubmissionConfirmed => _settings.SubmissionNotificationTemplateId,
            NotificationType.DataCheckConfirmed => _settings.PupilDataCheckConfirmTemplateId,
            NotificationType.AmendmentWithdrawn => _settings.WithdrawNotificationTemplateId,
            NotificationType.DataCheckWithdrawn => _settings.PupilDataCheckWithdrawTemplateId,
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType))
        };

        _logger.LogInformation(
            "Sending {NotificationType} notifications for ref {ReferenceNumber} to {RecipientCount} recipient(s)",
            notificationType, referenceNumber, recipientEmails.Count);

        foreach (var email in recipientEmails)
        {
            try
            {
                await SendEmailAsync(email, referenceNumber, deadline, templateId, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send {NotificationType} notification to {RecipientEmail} for ref {ReferenceNumber}",
                    notificationType, email, referenceNumber);
            }
        }
    }

    public async Task SendDlqThresholdEmailAsync(string toEmail, int dlqDepth, int threshold)
    {
        try
        {
            var personalisation = new Dictionary<string, object>
            {
                ["dlq_depth"] = dlqDepth,
                ["threshold"] = threshold
            };

            _logger.LogInformation(
                "Sending Dead-letter Queue Threshold email to {ToEmail}",
                toEmail);

            await _client.SendEmailAsync(
                toEmail,
                _settings.DlqThresholdTemplateId,
                personalisation: personalisation);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send DLQ threshold notification to {RecipientEmail}",
                toEmail);
        }
    }

    private async Task SendEmailAsync(
        string toEmail,
        string refNumber,
        string deadline,
        string templateId,
        string? url)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber },
            { "deadline", deadline }
        };

        if (!string.IsNullOrEmpty(url))
        {
            personalisation["submit others url"] = url;
        }

        if (_resiliencePipeline is not null)
        {
            await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.SendEmailAsync(toEmail, templateId, personalisation),
                CancellationToken.None);
        }
        else
        {
            await _client.SendEmailAsync(toEmail, templateId, personalisation);
        }
    }
}