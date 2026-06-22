namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// The Rules engine pipeline stage (the processor itself), between the Rules-engine queue and the
// Zendesk queue in animation order. It has no working queue, so it links to the transactions page
// filtered to the RulesEvaluated stage — the per-stage view of submissions the engine has decided.
public sealed record RulesEngineStageNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesEngineStage;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Rules engine";
    public string Description => "Submissions the rules engine has evaluated (transactions filtered to Rules engine).";
    public string Url => "/admin/observability/transactions?stage=RulesEvaluated";
    public bool Enabled => true;
    public int Order => 30;
}
