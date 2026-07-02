using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Notify;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

/// <summary>
/// Producer side of notification emails. Runs on the request thread: it captures the
/// request-scoped values a send needs (signed-in user, and — for the submission email — a
/// link that requires <c>HttpContext</c> to generate), builds a self-contained
/// <see cref="EmailNotification"/> and hands it to <see cref="INotificationDispatcher"/>.
/// It performs no external I/O, so the request returns without waiting for email delivery;
/// recipient resolution and the GOV.UK Notify sends happen later in <see cref="NotificationSender"/>.
/// </summary>
public sealed class RequestNotificationService(
    ICurrentUserService currentUserService,
    IEmailLinkGenerator emailLinkGenerator,
    INotificationDispatcher dispatcher) : IRequestNotificationService
{
    public async Task NotifySubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, string referenceNumber)
    {
        var linkUrl = emailLinkGenerator.GenerateLink(
            "WhatToChange", "Index", new { windowId }, "SubmissionNotification");

        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.SubmissionConfirmed,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            LinkUrl = linkUrl,
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true
        });
    }

    public async Task NotifyDataCheckConfirmedAsync(DateTime deadlineDate, string referenceNumber)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.DataCheckConfirmed,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true
        });
    }

    public async Task NotifyAmendmentWithdrawnAsync(string referenceNumber, DateTime deadlineDate)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.AmendmentWithdrawn,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = false
        });
    }

    public async Task NotifyDataCheckWithdrawnAsync(string referenceNumber, DateTime deadlineDate)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.DataCheckWithdrawn,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true
        });
    }

    private static string FormatDeadline(DateTime endDate)
    {
        return $"{endDate.ToString("htt").ToLower()} on {endDate:dddd d MMMM yyyy}";
    }
}
