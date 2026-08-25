namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the dead-letter queue view (/admin/queues/dlq). Grouped under the
// top-level Messages container so admins reach both incoming-message surfaces — search
// feedback and DLQ — from one nav node, and their counts consolidate into the single
// top-bar Messages badge.
public sealed record DeadLetterQueueNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DeadLetterQueue;
    public string? ParentKey => AdminNavKeys.MessagesGroup;
    public string Title => "Dead Letter Queue";
    public string Description => "Inspect, redrive and purge dead-lettered messages.";
    public string Url => "/admin/queues/dlq";
    public bool Enabled => true;
    public int Order => 20;
}
