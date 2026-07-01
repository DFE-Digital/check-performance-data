using DfE.CheckPerformanceData.Web.PageTree;
using NSubstitute;
namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

public class PageNodePathValidatorTests
{
    private static PageNodePathValidator Sut(params string[] reserved)
    {
        var provider = Substitute.For<IReservedRouteProvider>();
        provider.ReservedFirstSegments().Returns(new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase));
        return new PageNodePathValidator(provider);
    }

    [Fact] public void Rejects_ReservedAppRoute() => Assert.False(Sut("help", "pupils").Validate("help/x").ok);
    [Fact] public void Rejects_HardReserved() => Assert.False(Sut().Validate("admin").ok);
    [Fact] public void Rejects_Empty() => Assert.False(Sut().Validate("").ok);
    [Fact] public void Rejects_BadChars() => Assert.False(Sut().Validate("Support Page").ok);
    [Fact] public void Allows_FreePath() => Assert.True(Sut("help").Validate("support/faq").ok);
}
