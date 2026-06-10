namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Admin nav entry for the queue and dead-letter admin surface, shown under the
// System administration group.
public sealed record RulesEngineNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngine;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Queues";
    public string Description => "Queue depth and latency, and the dead-letter queue.";
    public string Url => "/admin/queues";
    public bool Enabled => true;
    public int Order => 20;
}
