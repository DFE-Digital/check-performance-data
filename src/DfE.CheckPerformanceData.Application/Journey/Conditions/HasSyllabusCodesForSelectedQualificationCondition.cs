using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// Returns true if the selected qualification has at least one syllabus code.
/// Used to conditionally show/hide the syllabus code question on the missing qualification details page.
/// </summary>
public sealed class HasSyllabusCodesForSelectedQualificationCondition : IJourneyCondition
{
    public string Name => "qualification.hasSyllabusCodes";

    public bool Evaluate(JourneyConditionContext ctx)
    {
        return ctx.Journey.SelectedQualification?.SyllabusCodes.Count > 0;
    }
}
