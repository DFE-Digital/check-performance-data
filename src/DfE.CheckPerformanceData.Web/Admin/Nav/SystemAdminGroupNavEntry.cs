namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Group descriptor: top-level System administration section. ParentKey is null so the
// landing-page controller treats it as a group container. Operational and system-level
// tooling clusters under this group (visual regression, rules engine, future audit
// log / env info / deployment status surfaces).
public sealed record SystemAdminGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SystemAdmin;
    public string? ParentKey => null;
    public string Title => "System administration";
    public string Description => "Observability and system-level tooling.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 20;
}
