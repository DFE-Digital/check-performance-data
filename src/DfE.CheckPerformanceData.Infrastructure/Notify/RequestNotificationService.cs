using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notify;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

public sealed class RequestNotificationService(
    INotifyService notifyService,
    IDfESignInApiClient dfESignInApiClient,
    ICurrentUserService currentUserService,
    IEmailLinkGenerator emailLinkGenerator) : IRequestNotificationService
{
    public async Task NotifySubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, string referenceNumber)
    {
        var recipients = await BuildNotificationRecipients();
        var deadline = FormatDeadline(deadlineDate);
        var linkUrl = emailLinkGenerator.GenerateLink(
            "WhatToChange", "Index", new { windowId }, "SubmissionNotification");

        await notifyService.SendNotificationsAsync(
            referenceNumber, deadline, recipients, NotificationType.SubmissionConfirmed, linkUrl);
    }

    public async Task NotifyDataCheckConfirmedAsync(DateTime deadlineDate, string referenceNumber)
    {
        var recipients = await BuildNotificationRecipients();
        var deadline = FormatDeadline(deadlineDate);

        await notifyService.SendNotificationsAsync(
            referenceNumber, deadline, recipients, NotificationType.DataCheckConfirmed);
    }

    public async Task NotifyAmendmentWithdrawnAsync(string referenceNumber)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentUserService.Email
        };

        await notifyService.SendNotificationsAsync(
            referenceNumber, string.Empty, recipients, NotificationType.AmendmentWithdrawn);
    }

    public async Task NotifyDataCheckWithdrawnAsync(string referenceNumber)
    {
        var recipients = await BuildNotificationRecipients();

        await notifyService.SendNotificationsAsync(
            referenceNumber, string.Empty, recipients, NotificationType.DataCheckWithdrawn);
    }

    private async Task<IReadOnlyCollection<string>> BuildNotificationRecipients()
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentUserService.Email
        };

        var orgUsers = await dfESignInApiClient.GetOrganisationUsersAsync(currentUserService.Ukprn);
        if (orgUsers?.Users is { Count: > 0 })
        {
            foreach (var user in orgUsers.Users)
            {
                recipients.Add(user.Email);
            }
        }

        return recipients;
    }

    private static string FormatDeadline(DateTime endDate)
    {
        return $"{endDate.ToString("htt").ToLower()} on {endDate:dddd d MMMM yyyy}";
    }
}
