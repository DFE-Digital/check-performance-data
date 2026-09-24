namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Top-level link. It was the only tile left under a "Window administration" group, so the group
// is gone and this entry stands by itself, like Dashboard.
public sealed record ManageWindowNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ManageWindow;
    public string? ParentKey => null;
    public string Title => "Manage windows";
    public string Description => "Browse and manage windows for the service.";
    public string Url => "/admin/windows";
    public bool Enabled => true;
    public int Order => 30;
}
