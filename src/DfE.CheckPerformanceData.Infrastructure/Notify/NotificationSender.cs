using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notify;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

/// <summary>
/// Consumer side of <see cref="EmailNotification"/>. Resolves the recipient set (including the
/// DfE Sign-in organisation-users lookup when requested) and sends via <see cref="INotifyService"/>.
/// Invoked by the background worker, off the request thread.
/// </summary>
public sealed class NotificationSender(
    INotifyService notifyService,
    IDfESignInApiClient dfESignInApiClient) : INotificationSender
{
    public async Task SendAsync(EmailNotification notification, CancellationToken cancellationToken = default)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            notification.OriginatorEmail
        };

        if (notification.IncludeOrganisationUsers)
        {
            var orgUsers = await dfESignInApiClient.GetOrganisationUsersAsync(notification.Ukprn);
            if (orgUsers?.Users is { Count: > 0 })
            {
                foreach (var user in orgUsers.Users)
                {
                    recipients.Add(user.Email);
                }
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
}
