namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the rules-engine working-queue list, nested under the Queues entry.
public sealed record RulesEngineQueueNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngineQueue;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Rules Engine Queue";
    public string Description => "Messages waiting for the rules engine to process.";
    public string Url => "/admin/queues/list/rules-engine";
    public bool Enabled => true;
    public int Order => 20;
}
