namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the dead-letter queue view (/admin/queues/dlq), nested under the
// Queues entry alongside the working-queue lists.
public sealed record DeadLetterQueueNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DeadLetterQueue;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Dead Letter Queue";
    public string Description => "Inspect, redrive and purge dead-lettered messages.";
    public string Url => "/admin/queues/dlq";
    public bool Enabled => true;
    public int Order => 30;
}
