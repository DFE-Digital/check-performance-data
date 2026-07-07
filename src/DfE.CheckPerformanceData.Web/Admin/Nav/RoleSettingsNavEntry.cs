namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry for the role-vs-section access grid. Lives under System administration.
public sealed record RoleSettingsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RoleSettings;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Role settings";
    public string Description => "Choose which admin sections each role can access.";
    public string Url => "/admin/system/roles";
    public bool Enabled => true;
    public int Order => 30;
}
