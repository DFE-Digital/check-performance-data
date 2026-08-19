using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

public enum NotificationType
{
    SubmissionConfirmed,
    BulkSubmissionConfirmed,
    DataCheckConfirmed,
    AmendmentWithdrawn,
    DataCheckWithdrawn,
    // AB#296648: confirmation that a 16-19 results enquiry was submitted. Appended last so existing
    // stored/serialised values are unmoved.
    ResultsEnquirySubmitted
}

/// <summary>
/// Service for sending emails via GOV.UK Notify.
/// </summary>
public interface INotifyService
{
    /// <summary>
    /// Sends notification emails to all recipients for the given submission scenario.
    /// Failures are logged but do not throw — the caller's outcome is independent
    /// of email delivery.
    /// </summary>
    /// <param name="referenceNumber">Submission reference number.</param>
    /// <param name="deadline">Display-friendly deadline text.</param>
    /// <param name="recipientEmails">Deduplicated recipient email addresses.</param>
    /// <param name="notificationType">Which notification template to use.</param>
    /// <param name="url">Optional URL (e.g. "submit others" or withdrawal link).</param>
    /// <param name="referenceNumbers">
    /// For a consolidated bulk submission email: every reference in the batch, listed in the
    /// email body. Null/empty for single-reference notifications (which use
    /// <paramref name="referenceNumber"/>).
    /// </param>
    Task SendNotificationsAsync(
        string referenceNumber,
        string deadline,
        IReadOnlyCollection<string> recipientEmails,
        NotificationType notificationType,
        string? url = null,
        IReadOnlyCollection<string>? referenceNumbers = null);

    /// <summary>
    /// Sends a dead-letter queue threshold alert.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="dlqDepth">Current depth of the dead-letter queue.</param>
    /// <param name="threshold">Alert threshold for the dead-letter queue.</param>
    Task SendDlqThresholdEmailAsync(string toEmail, int dlqDepth, int threshold);
}