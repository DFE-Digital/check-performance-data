namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Outcome of evaluating a <see cref="RuleSet"/> against a <see cref="RuleContext"/>.
/// Matches the docx's <c>Auto_Approved</c> / <c>Auto_Rejected</c> / <c>Scrutiny</c> values.
/// </summary>
public enum DecisionStatus
{
    AutoApproved,
    AutoRejected,
    Scrutiny
}
