namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Scans a rule set for window-relative date literals (#800). The worker uses it to decide
/// whether a message that carries no PupilData exercise start date needs a warning: only a
/// rule set with at least one relative literal is affected by the missing anchor.
/// </summary>
public static class RuleSetRelativeDateExtensions
{
    public static bool UsesWindowRelativeDates(this RuleSet rules) =>
        rules.Outcomes.SelectMany(o => o.Rules).Any(b => PredicateUsesRelativeDate(b.When));

    private static bool PredicateUsesRelativeDate(Predicate predicate) => predicate switch
    {
        Predicate.AllOf all => all.Items.Any(PredicateUsesRelativeDate),
        Predicate.AnyOf any => any.Items.Any(PredicateUsesRelativeDate),
        Predicate.Not not => PredicateUsesRelativeDate(not.Inner),
        Predicate.FieldEq eq => eq.Value is FieldValue.RelativeDate,
        Predicate.FieldNeq neq => neq.Value is FieldValue.RelativeDate,
        Predicate.FieldCompare cmp => cmp.Value is FieldValue.RelativeDate,
        Predicate.FieldIn inP => inP.Values.Any(v => v is FieldValue.RelativeDate),
        _ => false
    };
}
