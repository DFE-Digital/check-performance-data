namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Direct link to the Zendesk working-queue list, nested under the Queues entry.
public sealed record ZendeskQueueNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ZendeskQueue;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Zendesk Queue";
    public string Description => "Messages waiting to be raised as Zendesk tickets.";
    public string Url => "/admin/queues/list/zendesk";
    public bool Enabled => true;
    public int Order => 40;
}
