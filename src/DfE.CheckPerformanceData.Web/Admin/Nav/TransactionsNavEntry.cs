namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Tile linking the per-transaction view, nested under the Pipeline dashboard entry in the
// rules-engine sub-tree. Always-on (role-gated cypmd_admin on the controller); it lists the
// individual messages flowing through the processing pipeline with their stage and decision.
public sealed record TransactionsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.Transactions;
    public string? ParentKey => AdminNavKeys.Observability;
    public string Title => "Transactions";
    public string Description => "Per-message view of pipeline transactions and their decisions.";
    public string Url => "/admin/observability/transactions";
    public bool Enabled => true;
    public int Order => 20;
}
