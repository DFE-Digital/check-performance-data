namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Settings controlling <see cref="BlobRulesProvider"/>. Bound from the
/// <c>RulesEngineOptions</c> configuration section in the worker so the rules
/// config travels alongside the queue config.
/// </summary>
public sealed class BlobRulesProviderOptions
{
    public const string SectionName = "RulesEngineOptions";

    /// <summary>Private blob container holding the rules + lookups JSON.</summary>
    public string RulesBlobContainer { get; set; } = "rules-config";

    /// <summary>Blob name for the top-level <c>RuleSet</c> document.</summary>
    public string RulesBlobName { get; set; } = "rules.json";

    /// <summary>Blob name for the static country → official languages map.</summary>
    public string LookupsBlobName { get; set; } = "country-languages.json";

    /// <summary>Time between refresh attempts. Each attempt sends an If-None-Match using the last good ETag.</summary>
    public int RefreshIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// If refresh attempts keep failing past this many seconds, the provider
    /// marks <see cref="Application.RulesEngine.RulesHealth.StaleLastKnownGood"/>
    /// so the health check can degrade readiness.
    /// </summary>
    public int StalenessWarningSeconds { get; set; } = 1800;

    /// <summary>
    /// When true (default), the worker uploads the image-bundled seed JSON for any rules-config
    /// blob that does not yet exist on startup (see <see cref="RulesConfigSeeder"/>). Existing
    /// blobs are never overwritten. Set false to opt a deployment out of self-seeding.
    /// </summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>
    /// Directory holding the bundled seed JSON. The file names must match
    /// <see cref="RulesBlobName"/> / <see cref="LookupsBlobName"/>. Defaults to the
    /// <c>seed</c> folder next to the worker binary when null/empty.
    /// </summary>
    public string? SeedDirectory { get; set; }
}
