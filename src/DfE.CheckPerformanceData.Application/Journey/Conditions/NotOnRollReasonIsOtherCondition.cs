namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// True when the 16-19 "Not on roll" removal reason is "Other".
///
/// The four not-on-roll reasons share one evidence page. Apprentice, external candidate and
/// international student are self-explanatory removals, so evidence there is entirely optional;
/// "Other" says nothing on its own, so that path must carry either a file or an explanation —
/// which is the page's requireAtLeastOne rule, gated on this condition.
///
/// Fails closed on a missing answer (the rule stays on): the page is only reachable after the
/// reason has been answered, so no answer means something unexpected happened.
/// </summary>
public sealed class NotOnRollReasonIsOtherCondition : IJourneyCondition
{
    private const string ReasonQuestionId = "not-on-roll-reason";
    private const string OtherValue = "other";

    public string Name => "NotOnRollReasonIsOther";

    public bool Evaluate(JourneyConditionContext ctx)
    {
        // No answer at all, or a blank one: fail closed and keep the evidence rule on. The page
        // is only reachable once the reason is answered, so this cannot be a normal journey.
        if (!ctx.Journey.QuestionAnswers.TryGetValue(ReasonQuestionId, out var reason)
            || string.IsNullOrWhiteSpace(reason.TextValue))
            return true;

        return string.Equals(reason.TextValue, OtherValue, StringComparison.Ordinal);
    }
}
