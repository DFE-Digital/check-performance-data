namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record SearchAdminNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SearchAnalytics;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Search analytics";
    public string Description => "Search volume, top queries, zero-result content gaps, and per-session drill-ins.";
    public string Url => "/admin/Search";
    public bool Enabled => true;
    public int Order => 60;
}
