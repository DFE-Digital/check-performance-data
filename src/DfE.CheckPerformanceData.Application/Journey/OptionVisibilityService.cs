namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class OptionVisibilityService(IEnumerable<IJourneyCondition> conditions)
    : IOptionVisibilityService
{
    private readonly JourneyConditionEvaluator _evaluator = new(conditions);

    public IReadOnlyList<QuestionOption> GetVisibleOptions(Question question, JourneyConditionContext ctx)
    {
        if (question.Options is not { } options)
            return [];

        return options.Where(option => IsVisible(option.VisibleWhen, ctx)).ToList();
    }

    public bool IsQuestionVisible(Question question, JourneyConditionContext ctx)
        => IsVisible(question.VisibleWhen, ctx);

    // Every entry must hold (AND). An undecided entry hides (fail closed).
    private bool IsVisible(IReadOnlyList<JourneyConditionRef>? visibleWhen, JourneyConditionContext ctx)
        => visibleWhen is not { Count: > 0 } refs || _evaluator.AllHold(refs, ctx, whenUndecided: false);
}
