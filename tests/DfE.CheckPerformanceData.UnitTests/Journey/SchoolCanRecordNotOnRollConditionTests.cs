using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#304119: "Not on roll" shows to an independent school or to a listed FE college.
public class SchoolCanRecordNotOnRollConditionTests
{
    private static SchoolCanRecordNotOnRollCondition Sut(params string[] laestabs)
    {
        var colleges = laestabs.Select(l => $$"""{ "laestab": "{{l}}" }""");
        var list = NotOnRollCollegeList.Parse($$"""{ "colleges": [ {{string.Join(",", colleges)}} ] }""");
        var provider = Substitute.For<INotOnRollCollegeListProvider>();
        provider.Current.Returns(list);
        return new SchoolCanRecordNotOnRollCondition(provider);
    }

    private static JourneyConditionContext Context(string? typeId = null, string? laestab = null) => new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext { OrganisationTypeId = typeId, OrganisationLaestab = laestab }
    };

    [Fact]
    public void Name_is_SchoolCanRecordNotOnRoll()
    {
        Assert.Equal("SchoolCanRecordNotOnRoll", Sut().Name);
    }

    [Fact]
    public void True_for_an_independent_school_that_is_not_listed()
    {
        Assert.True(Sut("2118066").Evaluate(Context(typeId: "11", laestab: "9999999")));
    }

    [Fact]
    public void True_for_a_listed_college_that_is_not_independent()
    {
        Assert.True(Sut("2118066").Evaluate(Context(typeId: "18", laestab: "211/8066")));
    }

    [Fact]
    public void False_for_an_unlisted_school_that_is_not_independent()
    {
        Assert.False(Sut("2118066").Evaluate(Context(typeId: "01", laestab: "933/4290")));
    }

    [Fact]
    public void False_for_independent_special_school_type_10()
    {
        // The independent rule is unchanged: type 10 is still excluded.
        Assert.False(Sut().Evaluate(Context(typeId: "10", laestab: "933/4290")));
    }

    [Fact]
    public void False_when_the_school_has_no_laestab_or_type()
    {
        Assert.False(Sut("2118066").Evaluate(Context()));
    }

    [Fact]
    public void The_context_factory_carries_the_laestab_claim()
    {
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationLaestab.Returns("211/8066");

        var ctx = JourneyConditionContextFactory.Create(new RequestState(), user);

        Assert.Equal("211/8066", ctx.User.OrganisationLaestab);
    }
}
