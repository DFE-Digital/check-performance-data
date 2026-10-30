namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// A named date that a flow file compares a date answer with, in a <c>dateRules</c> entry
/// (<c>{ "check": "onOrAfter", "fact": "cohort.start" }</c>). Use one only for a date that the
/// journey's answers do not hold. The code gives the date, and the flow file holds the rule.
/// Register one implementation per fact in DI.
/// </summary>
public interface IJourneyDateFact
{
    /// <summary>The name referenced from JSON, e.g. "cohort.start".</summary>
    string Name { get; }

    /// <summary>
    /// The date, or null when the code cannot give one. A null value skips the rule and logs a
    /// warning. <paramref name="today"/> is the UK calendar date that the evaluator uses for every rule.
    /// </summary>
    DateOnly? Value(JourneyConditionContext ctx, DateOnly today);
}
