namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the dead-letter queue view (/admin/queues/dlq), shown under the
// System administration group immediately after the Queues tile so operators can
// jump straight to the dead-letter queue.
public sealed record DeadLetterQueueNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DeadLetterQueue;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Dead letter queue";
    public string Description => "Inspect, redrive and purge dead-lettered messages.";
    public string Url => "/admin/queues/dlq";
    public bool Enabled => true;
    public int Order => 25;
}
