namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public record ManageWindowNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.NewWindow;
    public string? ParentKey => AdminNavKeys.WindowAdmin;
    public string Title => "Manage windows";
    public string Description => "Browse and manage windows for the service.";
    public string Url => "/admin/windows";
    public bool Enabled => true;
    public int Order => 30;
}