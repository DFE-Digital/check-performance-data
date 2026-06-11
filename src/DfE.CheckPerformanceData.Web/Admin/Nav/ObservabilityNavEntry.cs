namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the observability dashboard (/admin/observability), shown under the System
// administration group. Always-on (role-gated cypmd_admin on the controller); it surfaces the
// health strip, throughput and decision-mix charts and the per-message journey timeline.
public sealed record ObservabilityNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.Observability;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Pipeline dashboard";
    public string Description => "Live queue health, throughput and decision-mix for the processing pipeline.";
    public string Url => "/admin/observability";
    public bool Enabled => true;
    public int Order => 23;
}
