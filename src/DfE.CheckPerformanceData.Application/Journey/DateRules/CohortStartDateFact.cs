namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// The earliest date that a pupil can have started at the school: 1 September of the school year
/// four years back. The rules engine applies the same boundary to the same field: the KS4 EAL
/// new-admission branches in <c>RulesEngineWorker/seed/rules.json</c> use
/// <c>firstSeptemberMinusYears: 4</c> against the window's PupilData exercise start date, which
/// is this value for a 2026 checking window (1 September 2022). If one changes, change the
/// other too. <c>CohortStartDateFactTests</c> pins the 2026 value and
/// <c>Ks4RemovalRulesTests</c> pins the seed's resolved boundary.
/// </summary>
public sealed class CohortStartDateFact : IJourneyDateFact
{
    public string Name => "cohort.start";

    public DateOnly? Value(JourneyConditionContext ctx, DateOnly today) => new(today.Year - 4, 9, 1);
}
