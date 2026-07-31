namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry for the engagement & amendment metrics dashboard. Top-level tile.
public sealed record DashboardNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.Dashboard;
    public string? ParentKey => null;
    public string Title => "Dashboard";
    public string Description => "School engagement and amendment request metrics for open checking windows.";
    public string Url => "/admin/dashboard";
    public bool Enabled => true;
    public int Order => 5;
}
