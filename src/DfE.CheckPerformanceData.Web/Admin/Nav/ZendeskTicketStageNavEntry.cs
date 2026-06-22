namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// The Zendesk ticket pipeline stage (the terminal step), after the Zendesk queue in animation
// order. It has no working queue, so it links to the transactions page filtered to the
// TicketCreated stage — the per-stage view of submissions that reached a Zendesk ticket.
public sealed record ZendeskTicketStageNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ZendeskTicketStage;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Zendesk ticket";
    public string Description => "Submissions that reached a Zendesk ticket (transactions filtered to Zendesk ticket).";
    public string Url => "/admin/observability/transactions?stage=TicketCreated";
    public bool Enabled => true;
    public int Order => 50;
}
