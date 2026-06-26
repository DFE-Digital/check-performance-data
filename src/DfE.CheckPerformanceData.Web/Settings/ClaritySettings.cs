namespace DfE.CheckPerformanceData.Web.Settings;

public record ClaritySettings
{
    public bool Enabled { get; init; }
    public string ProjectId { get; init; } = string.Empty;
}
