namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Sub-group descriptor nested under System administration. Clusters the rules-engine
// pipeline surfaces (dashboard, queues, configuration) one level deeper than the
// top-level System administration group. Url is empty because it is a container, not a
// navigable page; the renderer recurses into its descendants.
public sealed record RulesEngineGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngineGroup;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Rules Engine";
    public string Description => "The decision pipeline: dashboard, queues and configuration.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 10;
}
