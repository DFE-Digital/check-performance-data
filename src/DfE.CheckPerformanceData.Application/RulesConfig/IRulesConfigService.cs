using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>Read/edit/version the rules and lookups configs for the admin editor.</summary>
public interface IRulesConfigService
{
    Task<(RuleSet Rules, string? ETag)> GetRulesAsync(CancellationToken ct = default);
    Task<(Lookups Lookups, string? ETag)> GetLookupsAsync(CancellationToken ct = default);
    Task<RulesConfigSaveResult> SaveRulesAsync(RuleSet rules, string? expectedETag, CancellationToken ct = default);
    Task<RulesConfigSaveResult> SaveLookupsAsync(Lookups lookups, string? expectedETag, CancellationToken ct = default);
    Task<IReadOnlyList<RulesConfigVersionDto>> ListVersionsAsync(RulesConfigType type, CancellationToken ct = default);
    Task<RulesConfigSaveResult> RollbackAsync(int versionId, string? expectedETag, CancellationToken ct = default);
}
