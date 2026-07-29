namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class OptionVisibilityService(IEnumerable<IJourneyCondition> conditions)
    : IOptionVisibilityService
{
    public IReadOnlyList<QuestionOption> GetVisibleOptions(Question question, JourneyConditionContext ctx)
    {
        if (question.Options is not { } options)
            return [];

        return options.Where(option => IsVisible(option, ctx)).ToList();
    }

    private bool IsVisible(QuestionOption option, JourneyConditionContext ctx)
    {
        if (option.VisibleWhen is not { Count: > 0 } names)
            return true;

        // Every named condition must be registered and evaluate true (AND);
        // an unregistered name hides the option (fail closed).
        return names.All(name =>
            conditions.FirstOrDefault(c => c.Name == name) is { } condition && condition.Evaluate(ctx));
    }
}
