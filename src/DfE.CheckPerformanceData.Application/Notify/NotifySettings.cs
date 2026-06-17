using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Application.Notify;

public class NotifySettings
{
    public const string SectionName = "Notify";

    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string PupilDataCheckConfirmTemplateId { get; set; } = null!;

    [Required]
    public string PupilDataCheckWithdrawTemplateId { get; set; } = null!;

    [Required]
    public string SubmissionNotificationTemplateId { get; set; } = null!;

    [Required]
    public string WithdrawNotificationTemplateId { get; set; } = null!;

    [Required]
    public string DeadlineText { get; set; } = null!;

    public string? LinkBaseUrl { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public Dictionary<string, string>? UtmCampaigns { get; set; }
}