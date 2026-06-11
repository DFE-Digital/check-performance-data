using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notify.Client;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

/// <summary>
/// Implementation of <see cref="INotifyService"/> that sends emails via GOV.UK Notify.
/// </summary>
public class NotifyService : INotifyService
{
    private readonly NotificationClient _client;
    private readonly NotifySettings _settings;
    private readonly ILogger<NotifyService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotifyService"/> class.
    /// </summary>
    /// <param name="client">The GOV.UK Notify client.</param>
    /// <param name="settings">Notify configuration settings.</param>
    /// <param name="logger">Logger instance.</param>
    public NotifyService(
        NotificationClient client,
        IOptions<NotifySettings> settings,
        ILogger<NotifyService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendPupilDataCheckConfirmAsync(string toEmail, string refNumber, string deadline)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber },
            { "deadline", deadline }
        };

        _logger.LogInformation(
            "Sending Pupil Data Check Confirm email to {ToEmail} with ref {RefNumber} and deadline {Deadline}",
            toEmail, refNumber, deadline);

        await _client.SendEmailAsync(
            toEmail,
            _settings.PupilDataCheckConfirmTemplateId,
            personalisation: personalisation);
    }

    public async Task SendPupilDataCheckWithdrawAsync(string toEmail, string refNumber, string deadline)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber },
            { "deadline", deadline }
        };

        _logger.LogInformation(
            "Sending Pupil Data Check Withdraw email to {ToEmail} with ref {RefNumber} and deadline {Deadline}",
            toEmail, refNumber, deadline);

        await _client.SendEmailAsync(
            toEmail,
            _settings.PupilDataCheckWithdrawTemplateId,
            personalisation: personalisation);
    }

    public async Task SendSubmissionNotificationAsync(string toEmail, string refNumber, string deadline, string? submitOthersUrl = null)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber },
            { "deadline", deadline }
        };

        if (!string.IsNullOrEmpty(submitOthersUrl))
        {
            personalisation["submit others url"] = submitOthersUrl;
        }

        _logger.LogInformation(
            "Sending Submission Notification email to {ToEmail} with ref {RefNumber} and deadline {Deadline}",
            toEmail, refNumber, deadline);

        await _client.SendEmailAsync(
            toEmail,
            _settings.SubmissionNotificationTemplateId,
            personalisation: personalisation);
    }

    public async Task SendWithdrawNotificationAsync(string toEmail, string refNumber, string deadline, string? url = null)
    {
        var personalisation = new Dictionary<string, object>
        {
            { "email address", toEmail },
            { "ref number", refNumber },
            { "deadline", deadline }
        };

        if (!string.IsNullOrEmpty(url))
        {
            personalisation["url"] = url;
        }

        _logger.LogInformation(
            "Sending Withdraw Notification email to {ToEmail} with ref {RefNumber} and deadline {Deadline}",
            toEmail, refNumber, deadline);

        await _client.SendEmailAsync(
            toEmail,
            _settings.WithdrawNotificationTemplateId,
            personalisation: personalisation);
    }
}