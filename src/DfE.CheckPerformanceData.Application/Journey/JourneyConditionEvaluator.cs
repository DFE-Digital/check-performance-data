namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Evaluates a list of <see cref="JourneyConditionRef"/>s. Every entry must hold (AND).
/// <para>
/// An entry is undecided when it names no registered condition, or when it tests a question
/// that has no answer or a blank answer. An any-of entry is true when one of its entries is
/// true. Otherwise it is undecided when one of its entries is undecided. The caller says what
/// an undecided entry gives, so each use fails closed: <c>visibleWhen</c> hides,
/// <c>optionalWhen</c> keeps the question mandatory, and <c>requireAtLeastOneWhen</c> keeps the
/// rule on.
/// </para>
/// </summary>
public sealed class JourneyConditionEvaluator(IEnumerable<IJourneyCondition> conditions)
{
    public bool AllHold(IReadOnlyList<JourneyConditionRef> refs, JourneyConditionContext ctx, bool whenUndecided) =>
        refs.All(r => Evaluate(r, ctx) ?? whenUndecided);

    private bool? Evaluate(JourneyConditionRef condition, JourneyConditionContext ctx)
    {
        if (condition.IsAnyOf)
        {
            var results = condition.AnyOf!.Select(c => Evaluate(c, ctx)).ToList();
            if (results.Contains(true)) return true;
            return results.Contains(null) ? null : false;
        }

        if (condition.IsAnswerTest)
        {
            if (!ctx.Journey.QuestionAnswers.TryGetValue(condition.Answer!, out var answer)
                || string.IsNullOrWhiteSpace(answer.TextValue))
                return null;
            return condition.In?.Contains(answer.TextValue, StringComparer.Ordinal) == true;
        }

        return conditions.FirstOrDefault(c => c.Name == condition.Name) is { } named
            ? named.Evaluate(ctx) == condition.Is
            : null;
    }
}
