using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;

namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// True when the signed-in school is one of the FE colleges on the not-on-roll college list,
/// matched on LAESTAB (AB#304119). A flow file combines it with <c>school.isIndependent</c> in an
/// <c>anyOf</c> to offer the "Not on roll" reason.
/// </summary>
public sealed class SchoolIsOnNotOnRollListCondition(INotOnRollCollegeListProvider colleges)
    : IJourneyCondition
{
    public string Name => "school.isOnNotOnRollList";

    public bool Evaluate(JourneyConditionContext ctx) =>
        colleges.Current.Contains(ctx.User.OrganisationLaestab);
}
