using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class SchoolIsIndependentConditionTests
{
    private static JourneyConditionContext ContextWithTypeId(string? typeId) => new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext { OrganisationTypeId = typeId }
    };

    [Fact]
    public void Name_IsSchoolIsIndependent()
    {
        var sut = new SchoolIsIndependentCondition();
        Assert.Equal("SchoolIsIndependent", sut.Name);
    }

    [Fact]
    public void Evaluate_ReturnsTrue_WhenEstablishmentTypeIs11()
    {
        var sut = new SchoolIsIndependentCondition();
        Assert.True(sut.Evaluate(ContextWithTypeId("11")));
    }

    [Fact]
    public void Evaluate_ReturnsFalse_ForOtherIndependentSpecialSchoolType10()
    {
        // Type 10 (Other Independent Special School) is deliberately excluded.
        var sut = new SchoolIsIndependentCondition();
        Assert.False(sut.Evaluate(ContextWithTypeId("10")));
    }

    [Fact]
    public void Evaluate_ReturnsFalse_ForNonIndependentType()
    {
        var sut = new SchoolIsIndependentCondition();
        Assert.False(sut.Evaluate(ContextWithTypeId("01")));
    }

    [Fact]
    public void Evaluate_ReturnsFalse_WhenTypeIdMissing()
    {
        var sut = new SchoolIsIndependentCondition();
        Assert.False(sut.Evaluate(ContextWithTypeId(null)));
    }
}
