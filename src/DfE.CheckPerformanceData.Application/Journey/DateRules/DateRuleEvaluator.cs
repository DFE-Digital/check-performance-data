using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Runs the <c>dateRules</c> of each <c>Date</c> question on a page. See docs/question-flows.md.
/// <para>
/// The rules of a question run in list order, and the first rule that fails gives the message,
/// so a question gets at most one message. Put <c>notInFuture</c> first: a date that is wrong by
/// itself makes a comparison with it misleading.
/// </para>
/// <para>
/// A blank or invalid date takes part in no rule. The format rules report it, and the page shows
/// only the first error for each question. A comparison whose target has no value is skipped.
/// A date fact that is not registered or that gives null skips its rule and logs a warning.
/// </para>
/// </summary>
public sealed class DateRuleEvaluator(IEnumerable<IJourneyDateFact> facts, ILogger logger)
{
    private readonly IReadOnlyList<IJourneyDateFact> _facts = facts.ToList();

    /// <param name="answers">Every answer in the journey, with the page's posted answers on top.
    /// A rule can compare with a question on another page.</param>
    public IReadOnlyList<DateFieldViolation> Evaluate(
        JourneyPage page,
        IReadOnlyDictionary<string, QuestionAnswer> answers,
        JourneyConditionContext ctx,
        DateOnly today,
        string pupilName)
    {
        var violations = new List<DateFieldViolation>();

        foreach (var question in page.Questions)
        {
            if (question.Type != QuestionType.Date || question.DateRules is not { Count: > 0 } rules) continue;
            if (Answered(answers, question.Id) is not { } date) continue;

            foreach (var rule in rules)
            {
                if (!Fails(rule, date, answers, ctx, today, out var target)) continue;
                violations.Add(new DateFieldViolation(question.Id, Format(rule.Message, pupilName, target)));
                break;
            }
        }

        return violations;
    }

    private bool Fails(
        DateRule rule, DateOnly date, IReadOnlyDictionary<string, QuestionAnswer> answers,
        JourneyConditionContext ctx, DateOnly today, out DateOnly? target)
    {
        if (rule.Check == DateRuleCheck.NotInFuture)
        {
            target = null;
            return date > today;
        }

        target = Target(rule, answers, ctx, today);
        return rule.Check switch
        {
            DateRuleCheck.OnOrBefore => date > target,
            DateRuleCheck.OnOrAfter => date < target,
            _ => false
        };
    }

    // Lifted nullable comparison: with a null target, both "date > target" and "date < target"
    // give false, so a missing target skips the rule.
    private DateOnly? Target(
        DateRule rule, IReadOnlyDictionary<string, QuestionAnswer> answers, JourneyConditionContext ctx, DateOnly today)
    {
        if (rule.Question is not null) return Answered(answers, rule.Question);
        if (rule.Date is not null) return rule.Date;
        if (rule.Fact is null) return null;

        var value = _facts.FirstOrDefault(f => f.Name == rule.Fact)?.Value(ctx, today);
        if (value is null)
            logger.LogWarning("Date rule skipped: date fact {DateFact} is not registered or has no value", rule.Fact);
        return value;
    }

    private static DateOnly? Answered(IReadOnlyDictionary<string, QuestionAnswer> answers, string questionId) =>
        answers.TryGetValue(questionId, out var answer) ? answer.DateValue?.ToDateOnly() : null;

    // "d MMMM yyyy" (for example "27 March 2026") matches the summary page. The invariant culture
    // stops the message changing with the server culture.
    private static string Format(string message, string pupilName, DateOnly? target) =>
        JourneyTemplate.Resolve(message, pupilName)
            .Replace(
                "{date}",
                target?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal);
}
