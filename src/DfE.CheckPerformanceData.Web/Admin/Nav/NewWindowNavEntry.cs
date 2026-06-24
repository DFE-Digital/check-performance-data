namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record NewWindowNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.NewWindow;
    public string? ParentKey => AdminNavKeys.WindowAdmin;
    public string Title => "Create new window";
    public string Description => "Browse and manage windows for the service.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 30;
}