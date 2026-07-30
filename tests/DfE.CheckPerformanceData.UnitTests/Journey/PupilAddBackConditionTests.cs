using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class PupilAddBackConditionTests
{
    private static JourneyConditionContext ContextWithPincl(int? pincl) => new()
    {
        Journey = new RequestState
        {
            SelectedPupil = pincl is { } code
                ? new PupilDto
                {
                    Id = Guid.NewGuid(), Firstname = "Test", Surname = "Pupil",
                    Sex = "F", DateOfBirth = "01/09/2010", Age = 15,
                    Cypmd_Id = "C1", Upn = "U1", Pincl = code
                }
                : null
        },
        User = new JourneyUserContext()
    };

    [Fact]
    public void Names_MatchTheJsonContract()
    {
        Assert.Equal("PupilIsAddBack", new PupilIsAddBackCondition().Name);
        Assert.Equal("PupilIsNotAddBack", new PupilIsNotAddBackCondition().Name);
    }

    [Fact]
    public void AddBack_TrueOnlyFor403()
    {
        var sut = new PupilIsAddBackCondition();
        Assert.True(sut.Evaluate(ContextWithPincl(403)));
        Assert.False(sut.Evaluate(ContextWithPincl(401)));
        Assert.False(sut.Evaluate(ContextWithPincl(0)));
        Assert.False(sut.Evaluate(ContextWithPincl(null))); // no pupil picked
    }

    [Fact]
    public void NotAddBack_IsTheExactComplement()
    {
        var sut = new PupilIsNotAddBackCondition();
        Assert.False(sut.Evaluate(ContextWithPincl(403)));
        Assert.True(sut.Evaluate(ContextWithPincl(401)));
        Assert.True(sut.Evaluate(ContextWithPincl(0)));
        Assert.True(sut.Evaluate(ContextWithPincl(null)));
    }
}
