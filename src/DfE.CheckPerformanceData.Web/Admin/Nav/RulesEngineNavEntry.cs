namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Admin nav entry for the queue admin surface, nested under the Rules Engine sub-group.
// Parents the three queue children (rules-engine, zendesk, dead-letter).
public sealed record RulesEngineNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngine;
    public string? ParentKey => AdminNavKeys.RulesEngineGroup;
    public string Title => "Queues";
    public string Description => "Queue depth and latency, and the dead-letter queue.";
    public string Url => "/admin/queues";
    public bool Enabled => true;
    public int Order => 20;
}
