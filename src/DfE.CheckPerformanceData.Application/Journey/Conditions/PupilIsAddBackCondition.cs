using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// Shows options gated on the picked pupil having been "added back" into the
/// school's results by DfE (Pupil Inclusion Status Flag 403 — the same code the
/// rules engine uses via <see cref="AnswerFieldMap.AddBackPincl"/>). No pupil
/// picked, or Pincl unsupplied (0), counts as not add-back.
/// </summary>
public sealed class PupilIsAddBackCondition : IJourneyCondition
{
    public string Name => "PupilIsAddBack";

    public bool Evaluate(JourneyConditionContext ctx) =>
        ctx.Journey.SelectedPupil?.Pincl == AnswerFieldMap.AddBackPincl;
}
