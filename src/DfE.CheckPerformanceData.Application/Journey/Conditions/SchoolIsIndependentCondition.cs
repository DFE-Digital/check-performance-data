namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// Shows options gated on the signed-in school being an independent school,
/// identified by the GIAS establishment type id "11" (Other Independent
/// School). Type 10 (Other Independent Special School) is deliberately
/// excluded — ticket 281165 explicitly confirmed only type 11 should see this
/// reason; if special-independents are ever added, change the const to a set.
/// The type id is sourced from the DfE Sign-in get-organisations response
/// field <c>$.type.id</c>, carried through to <c>ctx.User</c>.
/// </summary>
public sealed class SchoolIsIndependentCondition : IJourneyCondition
{
    private const string OtherIndependentSchoolTypeId = "11";

    public string Name => "SchoolIsIndependent";

    public bool Evaluate(JourneyConditionContext ctx) =>
        ctx.User.OrganisationTypeId == OtherIndependentSchoolTypeId;
}
