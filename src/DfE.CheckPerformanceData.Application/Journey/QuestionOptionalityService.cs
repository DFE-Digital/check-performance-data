namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionOptionalityService(IEnumerable<IJourneyCondition> conditions)
    : IQuestionOptionalityService
{
    public IReadOnlySet<string> GetConditionallyOptionalQuestionIds(JourneyPage page, JourneyConditionContext ctx)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in page.Questions)
        {
            if (question.OptionalWhen is not { Count: > 0 } names) continue;
            if (names.All(name =>
                    conditions.FirstOrDefault(c => c.Name == name) is { } condition && condition.Evaluate(ctx)))
                ids.Add(question.Id);
        }
        return ids;
    }

    public bool IsRequireAtLeastOneActive(JourneyPage page, JourneyConditionContext ctx)
    {
        if (!page.RequireAtLeastOne) return false;
        if (page.RequireAtLeastOneWhen is not { Count: > 0 } names) return true;

        // Fail closed, as OptionalWhen does: an unregistered name keeps the rule on, so a
        // typo cannot silently drop a mandatory-evidence rule.
        return names.All(name =>
            conditions.FirstOrDefault(c => c.Name == name) is not { } condition || condition.Evaluate(ctx));
    }
}
