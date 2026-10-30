namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// The earliest date that a pupil can have started at the school: 1 September of the school year
/// four years back. The rules engine applies the same boundary to the same field
/// (<c>RulesEngineWorker/seed/rules.json</c> holds <c>{ "field": "schoolAdmissionDate", "gte": "2022-09-01" }</c>,
/// which is this value for a 2026 checking window). If one changes, change the other too.
/// <c>CohortStartDateFactTests</c> pins the 2026 value.
/// </summary>
public sealed class CohortStartDateFact : IJourneyDateFact
{
    public string Name => "cohort.start";

    public DateOnly? Value(JourneyConditionContext ctx, DateOnly today) => new(today.Year - 4, 9, 1);
}
