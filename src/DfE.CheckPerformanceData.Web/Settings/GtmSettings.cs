namespace DfE.CheckPerformanceData.Web.Settings;

public record GtmSettings
{
    public bool Enabled { get; init; }
    public string ContainerId { get; init; } = string.Empty;
    public string AuthKey { get; init; } = string.Empty;
    public string PreviewId { get; init; } = string.Empty;
}
