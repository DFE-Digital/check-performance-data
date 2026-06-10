using DfE.CheckPerformanceData.Application.RulesConfig;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// Append-only snapshot of a saved rules-config document. Mirrors ContentBlockVersion but is
/// standalone (no parent entity) and discriminated by ConfigType.
/// </summary>
public sealed class RulesConfigVersion
{
    public int Id { get; set; }
    public RulesConfigType ConfigType { get; set; }
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
