namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile linking to the CMS settings page (/admin/settings). Sits in the
// CMS administration group after the housekeeping tiles.
public sealed record CmsSettingsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.CmsSettings;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "CMS settings";
    public string Description => "Configure service settings such as the number of rows shown per page.";
    public string Url => "/admin/settings";
    public bool Enabled => true;
    public int Order => 50;
}
