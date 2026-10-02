using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class SchoolIsNotIndependentConditionTests
{
    private static JourneyConditionContext ContextWithTypeId(string? typeId) => new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext { OrganisationTypeId = typeId }
    };

    [Fact]
    public void Name_IsSchoolIsNotIndependent()
    {
        var sut = new SchoolIsNotIndependentCondition();
        Assert.Equal("SchoolIsNotIndependent", sut.Name);
    }

    [Fact]
    public void Evaluate_ReturnsFalse_WhenEstablishmentTypeIs11()
    {
        var sut = new SchoolIsNotIndependentCondition();
        Assert.False(sut.Evaluate(ContextWithTypeId("11")));
    }

    [Fact]
    public void Evaluate_ReturnsTrue_ForMaintainedSchoolType1()
    {
        var sut = new SchoolIsNotIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId("1")));
    }

    [Fact]
    public void Evaluate_ReturnsTrue_ForOtherIndependentSpecialSchoolType10()
    {
        // Type 10 (Other Independent Special School) is deliberately NOT independent for
        // journey purposes — ticket 281165 confirmed only type 11. This condition must inherit
        // that exclusion rather than widening it, so a type-10 school keeps seeing the reasons
        // the shipped SchoolIsIndependentCondition lets it see.
        var sut = new SchoolIsNotIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId("10")));
    }

    [Fact]
    public void Evaluate_ReturnsTrue_WhenTypeIdMissing()
    {
        var sut = new SchoolIsNotIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId(null)));
    }

    [Fact]
    public void Evaluate_ReturnsTrue_ForEmptyTypeId()
    {
        // CurrentUserService.OrganisationTypeId yields "" (not null) when the claim is absent,
        // so this is the shape a school with no organisation_type_id claim actually produces.
        var sut = new SchoolIsNotIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId("")));
    }

    [Fact]
    public void Evaluate_ReturnsTrue_ForNonIndependentType()
    {
        var sut = new SchoolIsNotIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId("01")));
    }

    [Fact]
    public void Evaluate_IsTheExactComplementOfTheIndependentCondition_OnEveryTypeId()
    {
        // The two independent-related reasons are used in opposite polarities on the same option
        // set, so they cannot disagree. Pinned here so a future edit to either one trips this
        // rather than the UI.
        var independent = new SchoolIsIndependentCondition();
        var sut = new SchoolIsNotIndependentCondition();

        foreach (var typeId in new string?[] { null, "", "1", "01", "10", "11", "111", "11 ", " 11", "12" })
            Assert.Equal(!independent.Evaluate(ContextWithTypeId(typeId)), sut.Evaluate(ContextWithTypeId(typeId)));
    }
}
