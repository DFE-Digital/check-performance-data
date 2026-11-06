using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

/// <summary>
/// Consumer side of <see cref="EmailNotification"/>. Resolves the recipient set (including the
/// DfE Sign-in organisation-users lookup when requested) and sends via <see cref="INotifyService"/>.
/// Invoked by the background worker, off the request thread.
/// </summary>
public sealed class NotificationSender(
    INotifyService notifyService,
    IDfESignInApiClient dfESignInApiClient,
    ILogger<NotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(EmailNotification notification, CancellationToken cancellationToken = default)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            notification.OriginatorEmail
        };

        if (notification.IncludeOrganisationUsers)
        {
            foreach (var email in await GetOrganisationUserEmailsAsync(notification))
            {
                recipients.Add(email);
            }
        }

        await notifyService.SendNotificationsAsync(
            notification.ReferenceNumber,
            notification.Deadline,
            recipients,
            notification.Type,
            new EmailSubstitutions(
                notification.CeName,
                notification.LearnerNoun,
                notification.TurnaroundCommitment),
            notification.LinkUrl,
            notification.ReferenceNumbers);
    }

    // DfE Sign-in finds an organisation's users by UKPRN or UPIN only. Many maintained primary
    // schools have neither. A failed lookup must not stop the email to the submitter.
    private async Task<IEnumerable<string>> GetOrganisationUserEmailsAsync(EmailNotification notification)
    {
        if (string.IsNullOrWhiteSpace(notification.Ukprn))
        {
            logger.LogWarning(
                "Notification {ReferenceNumber}: the organisation has no UKPRN. The email goes to the submitter only.",
                notification.ReferenceNumber);
            return [];
        }

        try
        {
            var orgUsers = await dfESignInApiClient.GetOrganisationUsersAsync(notification.Ukprn);
            return orgUsers?.Users.Select(u => u.Email) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Notification {ReferenceNumber}: the DfE Sign-in organisation users lookup failed. " +
                "The email goes to the submitter only.",
                notification.ReferenceNumber);
            return [];
        }
    }
}
