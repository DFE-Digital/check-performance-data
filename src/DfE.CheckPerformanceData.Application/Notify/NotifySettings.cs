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


    public string? LinkBaseUrl { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public Dictionary<string, string>? UtmCampaigns { get; set; }
}