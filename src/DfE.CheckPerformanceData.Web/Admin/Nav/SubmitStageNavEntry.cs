namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// The Submit pipeline stage, first in the animation order under Stages / Queues. Submit has no
// working queue of its own, so it links to the transactions page filtered to the Submitted stage —
// the per-stage view of the submissions that have entered the pipeline.
public sealed record SubmitStageNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SubmitStage;
    public string? ParentKey => AdminNavKeys.RulesEngine;
    public string Title => "Submit";
    public string Description => "Submissions that have entered the pipeline (transactions filtered to Submit).";
    public string Url => "/admin/observability/transactions?stage=Submitted";
    public bool Enabled => true;
    public int Order => 10;
}
