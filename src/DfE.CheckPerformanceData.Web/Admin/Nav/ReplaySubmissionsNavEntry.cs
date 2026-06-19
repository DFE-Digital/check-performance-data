namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Tile linking the submission replay view, nested under the Pipeline dashboard entry in the
// rules-engine sub-tree. Always-on (role-gated cypmd_admin on the controller); it lets an
// administrator re-run a stored submission back through the processing pipeline.
public sealed record ReplaySubmissionsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ReplaySubmissions;
    public string? ParentKey => AdminNavKeys.Observability;
    public string Title => "Replay";
    public string Description => "Re-run a stored submission back through the processing pipeline.";
    public string Url => "/admin/observability/submissions";
    public bool Enabled => true;
    public int Order => 30;
}
