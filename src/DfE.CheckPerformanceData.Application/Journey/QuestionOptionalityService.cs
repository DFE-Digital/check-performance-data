namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionOptionalityService(IEnumerable<IJourneyCondition> conditions)
    : IQuestionOptionalityService
{
    private readonly JourneyConditionEvaluator _evaluator = new(conditions);

    public IReadOnlySet<string> GetConditionallyOptionalQuestionIds(JourneyPage page, JourneyConditionContext ctx)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in page.Questions)
        {
            if (question.OptionalWhen is not { Count: > 0 } refs) continue;
            // An undecided entry keeps the question mandatory (fail closed).
            if (_evaluator.AllHold(refs, ctx, whenUndecided: false))
                ids.Add(question.Id);
        }
        return ids;
    }

    public bool IsRequireAtLeastOneActive(JourneyPage page, JourneyConditionContext ctx)
    {
        if (!page.RequireAtLeastOne) return false;
        if (page.RequireAtLeastOneWhen is not { Count: > 0 } refs) return true;

        // Fail closed, as OptionalWhen does: an undecided entry keeps the rule on, so a typo or
        // a missing answer cannot silently drop a mandatory-evidence rule.
        return _evaluator.AllHold(refs, ctx, whenUndecided: true);
    }
}
