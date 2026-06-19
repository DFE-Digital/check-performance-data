namespace DfE.CheckPerformanceData.Infrastructure.Notifications;

// GOV.UK Notify configuration. The API key is a secret sourced from Key Vault / configuration
// and is never written to logs. When the key is absent the client degrades to a logged
// warning so the service runs before Notify is provisioned.
public sealed class NotifySettings
{
    public const string SectionName = "Notify";

    public string? ApiKey { get; set; }

    public string? DlqAlertTemplateId { get; set; }
}
