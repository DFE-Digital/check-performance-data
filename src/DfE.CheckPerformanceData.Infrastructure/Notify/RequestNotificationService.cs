using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Options;

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
    INotificationDispatcher dispatcher,
    IOptions<NotifySettings> notifySettings) : IRequestNotificationService
{
    public async Task NotifySubmissionConfirmedAsync(
        Guid windowId, DateTime deadlineDate, string referenceNumber, EmailSubstitutions substitutions)
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
            IncludeOrganisationUsers = true,
            CeName = substitutions.CeName,
            LearnerNoun = substitutions.LearnerNoun,
            TurnaroundCommitment = substitutions.TurnaroundCommitment
        });
    }

    public async Task NotifyResultsEnquirySubmittedAsync(string referenceNumber)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.ResultsEnquirySubmitted,
            ReferenceNumber = referenceNumber,
            // No deadline: an enquiry is not something the school must come back and finish. The
            // template is worded accordingly.
            Deadline = string.Empty,
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            // The submitter only. Nothing is being asked of the rest of the school, so copying every
            // user would be noise.
            IncludeOrganisationUsers = false
        });
    }

    public async Task NotifyBulkSubmissionConfirmedAsync(
        Guid windowId, DateTime deadlineDate, IReadOnlyList<string> referenceNumbers, EmailSubstitutions substitutions)
    {
        if (referenceNumbers.Count == 0) return;

        var linkUrl = emailLinkGenerator.GenerateLink(
            "WhatToChange", "Index", new { windowId }, "SubmissionNotification");
        var deadline = FormatDeadline(deadlineDate);
        var threshold = notifySettings.Value.BulkConsolidationThreshold;

        if (referenceNumbers.Count < threshold)
        {
            // Parity with individual submissions: one SubmissionConfirmed email per reference.
            foreach (var reference in referenceNumbers)
            {
                await dispatcher.EnqueueAsync(new EmailNotification
                {
                    Type = NotificationType.SubmissionConfirmed,
                    ReferenceNumber = reference,
                    Deadline = deadline,
                    LinkUrl = linkUrl,
                    Ukprn = currentUserService.Ukprn,
                    OriginatorEmail = currentUserService.Email,
                    IncludeOrganisationUsers = true,
                    CeName = substitutions.CeName,
                    LearnerNoun = substitutions.LearnerNoun,
                    TurnaroundCommitment = substitutions.TurnaroundCommitment
                });
            }
            return;
        }

        // One consolidated email listing every reference in the batch.
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.BulkSubmissionConfirmed,
            ReferenceNumber = string.Empty,
            ReferenceNumbers = referenceNumbers,
            Deadline = deadline,
            LinkUrl = linkUrl,
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true,
            CeName = substitutions.CeName,
            LearnerNoun = substitutions.LearnerNoun,
            TurnaroundCommitment = substitutions.TurnaroundCommitment
        });
    }

    public async Task NotifyDataCheckConfirmedAsync(
        DateTime deadlineDate, string referenceNumber, EmailSubstitutions substitutions)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.DataCheckConfirmed,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true,
            CeName = substitutions.CeName,
            LearnerNoun = substitutions.LearnerNoun,
            TurnaroundCommitment = substitutions.TurnaroundCommitment
        });
    }

    public async Task NotifyAmendmentWithdrawnAsync(
        string referenceNumber, DateTime deadlineDate, EmailSubstitutions substitutions)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.AmendmentWithdrawn,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = false,
            CeName = substitutions.CeName,
            LearnerNoun = substitutions.LearnerNoun,
            TurnaroundCommitment = substitutions.TurnaroundCommitment
        });
    }

    public async Task NotifyDataCheckWithdrawnAsync(
        string referenceNumber, DateTime deadlineDate, EmailSubstitutions substitutions)
    {
        await dispatcher.EnqueueAsync(new EmailNotification
        {
            Type = NotificationType.DataCheckWithdrawn,
            ReferenceNumber = referenceNumber,
            Deadline = FormatDeadline(deadlineDate),
            Ukprn = currentUserService.Ukprn,
            OriginatorEmail = currentUserService.Email,
            IncludeOrganisationUsers = true,
            CeName = substitutions.CeName,
            LearnerNoun = substitutions.LearnerNoun,
            TurnaroundCommitment = substitutions.TurnaroundCommitment
        });
    }

    private static string FormatDeadline(DateTime endDate)
    {
        return $"{endDate.ToString("htt").ToLower()} on {endDate:dddd d MMMM yyyy}";
    }
}
