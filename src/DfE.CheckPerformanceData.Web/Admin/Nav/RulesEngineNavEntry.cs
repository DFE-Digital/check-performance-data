namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Admin nav entry for the queue admin surface, nested under the Rules Engine sub-group.
// Parents the six pipeline-stage children in animation order: Submit, Rules-queue, Rules engine,
// Zendesk-queue, Zendesk ticket, Dead-letter queue (the three queues have their own list pages; the
// other three stages link to the transactions page filtered to that stage).
public sealed record RulesEngineNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngine;
    public string? ParentKey => AdminNavKeys.RulesEngineGroup;
    public string Title => "Stages / Queues";
    public string Description => "Each pipeline stage: the working queues and the per-stage views.";
    public string Url => "/admin/queues";
    public bool Enabled => true;
    public int Order => 20;
}
