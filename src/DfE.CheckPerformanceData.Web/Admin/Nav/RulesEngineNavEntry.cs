namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// rules engine admin surface ships; until then the landing page renders this as a
// "Coming soon" tile under the System administration group.
public sealed record RulesEngineNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngine;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Rules engine";
    public string Description => "Surfaces queue depth and recent activity once the rules engine worker lands.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 20;
}
