using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #439: the single source of truth behind the Include radio on What to change and the guard on
// the post it produces (FR-008). The Include option is supported on exactly the windows that
// already render it today — KS4June, KS4Autumn, KS2 — and is deliberately absent from Post16,
// where no Include_*.json exists and the option is a dead end.
public sealed class IncludeJourneyTests
{
    [Fact]
    public void SupportedWindowTypes_ContainsEveryWindowThatOffersInclude()
    {
        Assert.Contains(CheckingWindowType.KS4June, IncludeJourney.SupportedWindowTypes);
        Assert.Contains(CheckingWindowType.KS4Autumn, IncludeJourney.SupportedWindowTypes);
        Assert.Contains(CheckingWindowType.KS2, IncludeJourney.SupportedWindowTypes);
    }

    [Fact]
    public void SupportedWindowTypes_DoesNotContainPost16()
    {
        Assert.DoesNotContain(CheckingWindowType.Post16, IncludeJourney.SupportedWindowTypes);
    }

    [Fact]
    public void SupportedWindowTypes_ContainsExactlyTheExpectedMembers()
    {
        var expected = new HashSet<CheckingWindowType>
        {
            CheckingWindowType.KS4June,
            CheckingWindowType.KS4Autumn,
            CheckingWindowType.KS2
        };

        Assert.Equal(expected, IncludeJourney.SupportedWindowTypes);
    }
}