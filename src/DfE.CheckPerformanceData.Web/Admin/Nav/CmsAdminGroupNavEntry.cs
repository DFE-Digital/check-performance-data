namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Group descriptor: top-level CMS administration section. ParentKey is null so the
// landing-page controller treats it as a group container. Url is empty because groups
// are not directly navigable — children link to their own routes.
public sealed record CmsAdminGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.CmsAdmin;
    public string? ParentKey => null;
    public string Title => "CMS administration";
    public string Description => "Manage wiki content, retention and housekeeping.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 10;
}
