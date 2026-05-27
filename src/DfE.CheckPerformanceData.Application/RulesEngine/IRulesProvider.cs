namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Read-only view of the engine's currently-loaded rules. Implementations live
/// in Infrastructure (Blob-backed) and must never throw — on load failure they
/// return a <see cref="RulesSnapshot"/> with <see cref="RulesHealth.ColdFallback"/>
/// instead.
/// </summary>
public interface IRulesProvider
{
    RulesSnapshot Current { get; }
}

public sealed record RulesSnapshot(
    RuleSet Rules,
    Lookups Lookups,
    string Version,
    DateTimeOffset LoadedAt,
    RulesHealth Health);

public enum RulesHealth
{
    /// <summary>Most recent refresh succeeded.</summary>
    Healthy,
    /// <summary>Refresh has been failing past the staleness threshold; serving last-known-good.</summary>
    StaleLastKnownGood,
    /// <summary>Very first load failed; serving the hard-coded all-Scrutiny rule set.</summary>
    ColdFallback
}
