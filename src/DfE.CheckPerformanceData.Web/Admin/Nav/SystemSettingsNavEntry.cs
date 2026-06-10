namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile linking to the system settings page (/admin/settings). Sits in the
// System administration group: the page now holds service-wide settings (such as the
// dead-letter queue options) rather than CMS-only settings.
public sealed record SystemSettingsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SystemSettings;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "System settings";
    public string Description => "Configure service settings such as the number of rows shown per page and dead-letter queue options.";
    public string Url => "/admin/settings";
    public bool Enabled => true;
    public int Order => 40;
}
