using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins the single assembly point that feeds every journey condition its view of the signed-in
/// school. Both the GET-side option filtering (JourneyViewModelBuilder) and the POST-side selection
/// gate (JourneyController) build their context here, so this is the one place a mapping mistake
/// can reach both halves of option visibility.
///
/// It matters most for <c>OrganisationTypeId</c>, which decides the two independent-related reasons
/// in opposite polarities (<c>school.isIndependent</c> is true for "Not on roll" and false for
/// "Admitted following permanent exclusion"). If it were dropped here, every condition would see a
/// null type: "Not on roll" would silently vanish for every school AND "Admitted following permanent
/// exclusion" would silently stay visible for independent schools. Both failures are invisible - no
/// exception, no log, the page just shows the wrong reasons.
/// </summary>
public class JourneyConditionContextFactoryTests
{
    private static ICurrentUserService UserWithType(string typeId)
    {
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationTypeId.Returns(typeId);
        user.OrganisationId.Returns("org-1");
        user.OrganisationName.Returns("Kingsmead School");
        user.OrganisationUrn.Returns("142313");
        return user;
    }

    [Fact]
    public void Create_CarriesTheOrganisationTypeId()
    {
        var ctx = JourneyConditionContextFactory.Create(new RequestState(), UserWithType("11"));

        Assert.Equal("11", ctx.User.OrganisationTypeId);
    }

    [Fact]
    public void Create_CarriesTheOtherOrganisationFields()
    {
        var ctx = JourneyConditionContextFactory.Create(new RequestState(), UserWithType("1"));

        Assert.Equal("org-1", ctx.User.OrganisationId);
        Assert.Equal("Kingsmead School", ctx.User.OrganisationName);
        Assert.Equal("142313", ctx.User.OrganisationUrn);
    }

    [Fact]
    public void Create_CarriesTheJourneyState()
    {
        var journey = new RequestState { SelectedPupilId = "pupil-1" };

        var ctx = JourneyConditionContextFactory.Create(journey, UserWithType("1"));

        Assert.Same(journey, ctx.Journey);
    }

    [Fact]
    public void Create_MakesTheIndependentConditionSeeTheRealType()
    {
        var independent = new SchoolIsIndependentCondition();

        var forIndependent = JourneyConditionContextFactory.Create(new RequestState(), UserWithType("11"));
        Assert.True(independent.Evaluate(forIndependent));

        var forMaintained = JourneyConditionContextFactory.Create(new RequestState(), UserWithType("1"));
        Assert.False(independent.Evaluate(forMaintained));
    }

    [Fact]
    public void Create_CarriesTheLaestab()
    {
        var user = UserWithType("1");
        user.OrganisationLaestab.Returns("211/8066");

        var ctx = JourneyConditionContextFactory.Create(new RequestState(), user);

        Assert.Equal("211/8066", ctx.User.OrganisationLaestab);
    }
}
