using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Shared rules-config read for the admin pages: <see cref="AdminRulesController"/> and
/// <see cref="WindowRulesController"/> both show the same config, so they must treat a
/// missing or broken file the same way (one place to change the handling).
/// </summary>
public static class RulesConfigLookup
{
    /// <summary>
    /// Returns the current rules and the ETag to write against. Returns (null, null) when the
    /// config does not exist yet, so a page can offer the upload escape hatch.
    /// </summary>
    public static async Task<(RuleSet? Rules, string? ETag)> TryGetRulesAsync(
        IRulesConfigService rules,
        CancellationToken ct)
    {
        try
        {
            return await rules.GetRulesAsync(ct);
        }
        catch (RulesConfigNotFoundException)
        {
            return (null, null);
        }
    }
}
