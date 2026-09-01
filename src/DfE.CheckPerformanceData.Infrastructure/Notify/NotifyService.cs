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
        EmailSubstitutions substitutions,
        string? url = null,
        IReadOnlyCollection<string>? referenceNumbers = null)
    {
        var templateId = notificationType switch
        {
            NotificationType.SubmissionConfirmed => _settings.SubmissionNotificationTemplateId,
            NotificationType.BulkSubmissionConfirmed => _settings.BulkSubmissionNotificationTemplateId,
            NotificationType.DataCheckConfirmed => _settings.PupilDataCheckConfirmTemplateId,
            NotificationType.AmendmentWithdrawn => _settings.WithdrawNotificationTemplateId,
            NotificationType.DataCheckWithdrawn => _settings.PupilDataCheckWithdrawTemplateId,
            NotificationType.ResultsEnquirySubmitted => _settings.ResultsEnquirySubmittedTemplateId,
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType))
        };

        // A template id that is not configured yet — the results-enquiry template is an ops
        // prerequisite (AB#298309; content and runbook: docs/results-enquiry.md, "Confirmation
        // email") — would otherwise be sent to Notify once per recipient and rejected once per
        // recipient. One warning is the useful signal, and it keeps the caller's outcome
        // independent of email delivery as this interface promises.
        if (string.IsNullOrWhiteSpace(templateId))
        {
            _logger.LogWarning(
                "No GOV.UK Notify template is configured for {NotificationType}; skipping {RecipientCount} " +
                "email(s) for ref {ReferenceNumber}. Set the corresponding Notify:*TemplateId setting.",
                notificationType, recipientEmails.Count, referenceNumber);
            return;
        }

        _logger.LogInformation(
            "Sending {NotificationType} notifications for ref {ReferenceNumber} to {RecipientCount} recipient(s)",
            notificationType, referenceNumber, recipientEmails.Count);

        foreach (var email in recipientEmails)
        {
            try
            {
                await SendEmailAsync(email, referenceNumber, deadline, templateId, url, referenceNumbers, notificationType, substitutions);
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
        string? url,
        IReadOnlyCollection<string>? referenceNumbers,
        NotificationType notificationType,
        EmailSubstitutions substitutions)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber }
        };

        // The enquiry template (AB#298309) declares only ((ref number)): an enquiry has no
        // deadline — it is not something the school must come back and finish — so the key is
        // gated per type exactly as "ce name" and "learner noun" are below.
        if (notificationType is not NotificationType.ResultsEnquirySubmitted)
        {
            personalisation["deadline"] = deadline;
        }

        if (notificationType is not NotificationType.ResultsEnquirySubmitted
            && !string.IsNullOrEmpty(url))
        {
            personalisation["submit others url"] = url;
        }

        if (notificationType is not NotificationType.ResultsEnquirySubmitted
            && referenceNumbers is { Count: > 0 })
        {
            // Notify renders newline-joined text as separate lines / bullet items.
            personalisation["references"] = string.Join("\n", referenceNumbers);
        }

        // GOV.UK Notify fails the whole send when a template placeholder is missing from the
        // personalisation ("Missing personalisation", 400) and silently ignores extra keys. The
        // per-type gating here keeps the documented contract exact anyway: the runbook in
        // docs/results-enquiry.md must list every key a template may declare, and a template must
        // not be able to depend on a key the code might stop sending. Gate any new key per
        // notification type; never rely on a caller happening to pass null or empty.
        if (notificationType is not NotificationType.ResultsEnquirySubmitted)
        {
            personalisation["ce name"] = substitutions.CeName;
        }

        if (notificationType is NotificationType.DataCheckConfirmed
            or NotificationType.DataCheckWithdrawn
            or NotificationType.AmendmentWithdrawn)
        {
            personalisation["learner noun"] = substitutions.LearnerNoun;
        }

        if (notificationType is not NotificationType.ResultsEnquirySubmitted
            && !string.IsNullOrEmpty(substitutions.TurnaroundCommitment))
        {
            personalisation["turnaround commitment"] = substitutions.TurnaroundCommitment;
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