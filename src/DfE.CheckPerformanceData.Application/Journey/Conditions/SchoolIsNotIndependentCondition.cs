namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// The complement of <see cref="SchoolIsIndependentCondition"/>, gating options that
/// only apply to schools which are NOT registered independent schools — e.g. the KS4
/// Remove reason "Admitted following permanent exclusion (not registered independent
/// schools)", which an independent school cannot have and so cannot action.
///
/// Negates the sibling rather than restating the test, which keeps two facts true:
///
/// <list type="bullet">
/// <item>the string "11" stays written once in the codebase, so this condition cannot
/// drift into a second, independent definition of "independent"; and</item>
/// <item>the sibling's own doc invites a future edit — "if special-independents are ever
/// added, change the const to a set". Delegating means that one edit propagates here and
/// the two reasons cannot end up disagreeing about the same school. Type 10 stays
/// non-independent until that edit is made (ticket 281165).</item>
/// </list>
///
/// This is deliberately not constructor injection of the sibling, unlike the more obvious
/// reading of "the exact negation": QuestionFlowValidatorAlignmentTests instantiates every
/// condition with <c>Activator.CreateInstance</c>, which requires a public parameterless
/// constructor and throws <c>MissingMethodException</c> on an injected one. Note also that
/// the sibling pair <see cref="PupilIsAddBackCondition"/> / <see cref="PupilIsNotAddBackCondition"/>
/// restates its expression; that is safe there because the identifying value is a shared
/// <c>AnswerFieldMap</c> constant, whereas restating here would mean a second "11" literal.
/// </summary>
public sealed class SchoolIsNotIndependentCondition : IJourneyCondition
{
    private static readonly SchoolIsIndependentCondition Independent = new();

    public string Name => "SchoolIsNotIndependent";

    public bool Evaluate(JourneyConditionContext ctx) => !Independent.Evaluate(ctx);
}
