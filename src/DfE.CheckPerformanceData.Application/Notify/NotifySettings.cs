using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Application.Notify;

public class NotifySettings
{
    public const string SectionName = "Notify";

    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string FromAddress { get; set; } = null!;

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

    public string SubmitOthersUrl { get; set; } = null!;
}