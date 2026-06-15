using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>The posted state of the whole-branch editor. Round-trips through every postback.</summary>
public sealed class BranchEditForm
{
    public string OutcomeKey { get; set; } = string.Empty;
    public string OutcomeLabel { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Scrutiny;
    public string? LoadETag { get; set; }
    public List<PredicateNodeForm> Nodes { get; set; } = new();
    public string? Action { get; set; }
}
