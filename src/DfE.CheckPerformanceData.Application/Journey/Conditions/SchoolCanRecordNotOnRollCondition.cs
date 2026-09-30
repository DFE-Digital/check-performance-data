using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;

namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// Shows the KS4 June "Not on roll" removal reason to an independent school (the
/// <see cref="SchoolIsIndependentCondition"/> rule) or to one of the listed FE colleges,
/// matched on LAESTAB (AB#304119). One condition rather than two because
/// <c>visibleWhen</c> ANDs its names and this rule is an OR.
/// </summary>
public sealed class SchoolCanRecordNotOnRollCondition(INotOnRollCollegeListProvider colleges)
    : IJourneyCondition
{
    private static readonly SchoolIsIndependentCondition Independent = new();

    public string Name => "SchoolCanRecordNotOnRoll";

    public bool Evaluate(JourneyConditionContext ctx) =>
        Independent.Evaluate(ctx) || colleges.Current.Contains(ctx.User.OrganisationLaestab);
}
