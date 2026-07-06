namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile for the application logs viewer under System administration.
public sealed record AppLogsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.AppLogs;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "View logs";
    public string Description => "View, search, and download the ILogger events recorded by the app (Information and above by default). Newest events shown first.";
    public string Url => "/admin/system-administration/logs";
    public bool Enabled => true;
    public int Order => 25;
}
