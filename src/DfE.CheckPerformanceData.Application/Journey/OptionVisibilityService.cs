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
        if (string.IsNullOrEmpty(option.VisibleWhen))
            return true;

        var condition = conditions.FirstOrDefault(c => c.Name == option.VisibleWhen);
        return condition is not null && condition.Evaluate(ctx);
    }
}
