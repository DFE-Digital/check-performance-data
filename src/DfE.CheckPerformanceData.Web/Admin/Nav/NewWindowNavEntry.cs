namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record NewWindowNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.NewWindow;
    public string? ParentKey => AdminNavKeys.WindowAdmin;
    public string Title => "Create new window";
    public string Description => "Create new window for the service.";
    public string Url => "/admin/windows/title";
    public bool Enabled => true;
    public int Order => 30;
}