namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// FLAGGED copy (AB#294590): tile title and description.
public sealed record EgressRunsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.EgressRuns;
    public string? ParentKey => AdminNavKeys.EgressGroup;
    public string Title => "Egress runs";
    public string Description => "A history of every data egress run, with filters by checking window and status.";
    public string Url => "/admin/egress/runs";
    public bool Enabled => true;
    public int Order => 20;
}
