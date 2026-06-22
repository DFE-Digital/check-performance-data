using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

/// <summary>
/// Service for sending emails via GOV.UK Notify.
/// </summary>
public interface INotifyService
{
    /// <summary>
    /// Sends a "Confirm pupil data is correct" submission notification.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="refNumber">Reference number for the request.</param>
    /// <param name="deadline">Deadline for the action.</param>
    Task SendPupilDataCheckConfirmAsync(string toEmail, string refNumber, string deadline);

    /// <summary>
    /// Sends a "Confirm pupil data is correct" withdrawal notification.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="refNumber">Reference number for the request.</param>
    /// <param name="deadline">Deadline for the action.</param>
    Task SendPupilDataCheckWithdrawAsync(string toEmail, string refNumber, string deadline);

    /// <summary>
    /// Sends a Submission Notification (single request record).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="refNumber">Reference number for the request.</param>
    /// <param name="deadline">Deadline for the action.</param>
    /// <param name="submitOthersUrl">Optional URL to submit other requests.</param>
    Task SendSubmissionNotificationAsync(string toEmail, string refNumber, string deadline, string? submitOthersUrl = null);

    /// <summary>
    /// Sends a Withdraw Notification (single request record).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="refNumber">Reference number for the request.</param>
    /// <param name="deadline">Deadline for the action.</param>
    /// <param name="url">Optional dynamic URL related to the withdrawal.</param>
    Task SendWithdrawNotificationAsync(string toEmail, string refNumber, string deadline, string? url = null);

    /// <summary>
    /// Sends a dead-letter queue threshold alert.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="dlqDepth">Current depth of the dead-letter queue.</param>
    /// <param name="threshold">Alert threshold for the dead-letter queue.</param>
    Task SendDlqThresholdEmailAsync(string toEmail, int dlqDepth, int threshold);
}