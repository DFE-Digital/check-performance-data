namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Enabled tile linking to the read-only rules configuration surface (Milestone 2).
// Distinct from the disabled RulesEngineNavEntry, which is a placeholder for future
// queue-depth observability. Both live under the System administration group.
public sealed record RulesConfigNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesConfig;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Rules configuration";
    public string Description => "View the decision rules and country-language lookups, and their version history.";
    public string Url => "/admin/rules";
    public bool Enabled => true;
    public int Order => 30;
}
