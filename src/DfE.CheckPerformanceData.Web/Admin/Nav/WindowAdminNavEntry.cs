namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record WindowAdminNavEntry: IAdminNavEntry
{
    public string Key => AdminNavKeys.WindowAdmin;
    public string? ParentKey => null;
    public string Title => "Window administration";
    public string Description => "Browse and manage windows for the service.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 30;
}