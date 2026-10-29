using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#304119: the not-on-roll college list, matched on LAESTAB.
public class SchoolIsOnNotOnRollListConditionTests
{
    private static SchoolIsOnNotOnRollListCondition Sut(params string[] laestabs)
    {
        var colleges = laestabs.Select(l => $$"""{ "laestab": "{{l}}" }""");
        var list = NotOnRollCollegeList.Parse($$"""{ "colleges": [ {{string.Join(",", colleges)}} ] }""");
        var provider = Substitute.For<INotOnRollCollegeListProvider>();
        provider.Current.Returns(list);
        return new SchoolIsOnNotOnRollListCondition(provider);
    }

    private static JourneyConditionContext Context(string? laestab) => new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext { OrganisationLaestab = laestab }
    };

    [Fact]
    public void Name_MatchesTheJsonContract()
    {
        Assert.Equal("school.isOnNotOnRollList", Sut().Name);
    }

    [Fact]
    public void True_for_a_listed_college()
    {
        Assert.True(Sut("2118066").Evaluate(Context("211/8066")));
    }

    [Fact]
    public void False_for_an_unlisted_school()
    {
        Assert.False(Sut("2118066").Evaluate(Context("933/4290")));
    }

    [Fact]
    public void False_when_the_school_has_no_laestab()
    {
        Assert.False(Sut("2118066").Evaluate(Context(null)));
    }
}
