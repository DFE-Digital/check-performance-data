using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// The complement of <see cref="PupilIsAddBackCondition"/>: hides the standard
/// removal reasons from add-back (Pincl 403) pupils per PBI 292525. Defaults to
/// true when no pupil is picked or Pincl is unsupplied, so the standard reason
/// list is the fail-safe view.
/// </summary>
public sealed class PupilIsNotAddBackCondition : IJourneyCondition
{
    public string Name => "PupilIsNotAddBack";

    public bool Evaluate(JourneyConditionContext ctx) =>
        ctx.Journey.SelectedPupil?.Pincl != AnswerFieldMap.AddBackPincl;
}
