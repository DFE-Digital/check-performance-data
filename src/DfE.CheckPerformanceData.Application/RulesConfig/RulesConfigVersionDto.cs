namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed record RulesConfigVersionDto
{
    public int Id { get; init; }
    public RulesConfigType ConfigType { get; init; }
    public int VersionNumber { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
