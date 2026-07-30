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
}
