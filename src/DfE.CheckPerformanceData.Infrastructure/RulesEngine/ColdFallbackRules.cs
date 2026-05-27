using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// The deterministic rule set used when the very first load fails. Carries
/// no outcomes, so every incoming request resolves via
/// <see cref="Decision.UnmatchedOutcome"/> → Scrutiny. That's exactly the
/// "safe default" promised by the design's fallback policy.
/// </summary>
internal static class ColdFallbackRules
{
    public static RulesSnapshot Build(DateTimeOffset now) =>
        new(
            Rules: new RuleSet(
                Version: "cold-fallback",
                UpdatedAt: now,
                Outcomes: Array.Empty<OutcomeRules>()),
            Lookups: Lookups.Empty,
            Version: "cold-fallback",
            LoadedAt: now,
            Health: RulesHealth.ColdFallback);
}
