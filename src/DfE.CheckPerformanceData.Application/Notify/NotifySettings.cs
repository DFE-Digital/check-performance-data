using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Application.Notify;

public class NotifySettings
{
    public const string SectionName = "Notify";

    [Required]
    public string ApiKey { get; set; } = null!;


    public string PupilDataCheckConfirmTemplateId { get; set; } = null!;


    public string PupilDataCheckWithdrawTemplateId { get; set; } = null!;


    public string SubmissionNotificationTemplateId { get; set; } = null!;

    public string WithdrawNotificationTemplateId { get; set; } = null!;
    public string DlqThresholdTemplateId { get; set; } = null!;

    public string BulkSubmissionNotificationTemplateId { get; set; } = null!;

    /// <summary>
    /// AB#296648: confirms a submitted 16-19 results enquiry. Not <c>[Required]</c> — the team has yet
    /// to create this template, and an unset value must degrade to "no email sent, warning logged"
    /// rather than stopping the app from starting.
    /// </summary>
    public string ResultsEnquirySubmittedTemplateId { get; set; } = null!;

    /// <summary>
    /// Batch size at or above which a single consolidated submission email is sent instead of one
    /// email per request. Below it, individual emails are sent (parity with single submissions).
    /// </summary>
    public int BulkConsolidationThreshold { get; set; } = 5;


    public string? LinkBaseUrl { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public Dictionary<string, string>? UtmCampaigns { get; set; }
}