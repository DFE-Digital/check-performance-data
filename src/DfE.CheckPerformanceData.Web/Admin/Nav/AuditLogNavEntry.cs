namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// FLAGGED copy (AB#294592): tile title and description. A root entry with a Url and no children —
// the landing page renders it as a linked heading and the sidebar as a leaf (Dashboard precedent).
public sealed record AuditLogNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.AuditLog;
    public string? ParentKey => null;
    public string Title => "Audit log";
    public string Description => "A record of every data egress run and other administrative activity, with filters and a CSV export.";
    public string Url => "/admin/audit-log";
    public bool Enabled => true;
    public int Order => 40;
}
