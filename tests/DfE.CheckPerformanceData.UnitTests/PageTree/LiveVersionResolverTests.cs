using DfE.CheckPerformanceData.Application.PageTree;
namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class LiveVersionResolverTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Returns_Null_WhenNoVersionHasStarted()
    {
        int? live = LiveVersionResolver.Resolve(
            [new PageVersionWindow(1, Now.AddDays(1), null)], Now);
        Assert.Null(live);
    }

    [Fact]
    public void Returns_TheOnlyOpenVersion()
    {
        int? live = LiveVersionResolver.Resolve(
            [new PageVersionWindow(1, Now.AddDays(-1), null)], Now);
        Assert.Equal(1, live);
    }

    [Fact]
    public void Picks_LatestStarted_WhenMultipleOpen()
    {
        int? live = LiveVersionResolver.Resolve(
        [
            new PageVersionWindow(1, Now.AddDays(-10), null),
            new PageVersionWindow(2, Now.AddDays(-2), null),
            new PageVersionWindow(3, Now.AddDays(2), null),   // not yet live
        ], Now);
        Assert.Equal(2, live);
    }

    [Fact]
    public void Excludes_ExpiredWindows()
    {
        int? live = LiveVersionResolver.Resolve(
            [new PageVersionWindow(1, Now.AddDays(-10), Now.AddDays(-1))], Now);
        Assert.Null(live);
    }

    [Fact]
    public void Null_PublishFrom_IsNotLive()
    {
        // A version never scheduled (draft) is never live.
        int? live = LiveVersionResolver.Resolve(
            [new PageVersionWindow(1, null, null)], Now);
        Assert.Null(live);
    }
}
