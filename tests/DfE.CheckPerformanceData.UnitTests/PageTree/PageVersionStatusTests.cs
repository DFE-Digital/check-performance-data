using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// Pure unit tests for PageVersionStatus helper.
public sealed class PageVersionStatusTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Current_Version_IsLive_Green()
    {
        var st = PageVersionStatus.Of(isCurrent: true, publishFrom: DateTime.UtcNow.AddDays(-1), nowUtc: Now);

        Assert.Equal("Live", st.Label);
        Assert.Equal("govuk-tag--green", st.TagClass);
    }

    [Fact]
    public void FuturePublishFrom_NotCurrent_IsScheduled_Blue()
    {
        var st = PageVersionStatus.Of(isCurrent: false, publishFrom: Now.AddDays(1), nowUtc: Now);

        Assert.Equal("Scheduled", st.Label);
        Assert.Equal("govuk-tag--blue", st.TagClass);
    }

    [Fact]
    public void PastPublishFrom_NotCurrent_IsPast_Grey()
    {
        // A superseded version: had a publish window, no longer current.
        var st = PageVersionStatus.Of(isCurrent: false, publishFrom: Now.AddDays(-5), nowUtc: Now);

        Assert.Equal("Past", st.Label);
        Assert.Equal("govuk-tag--grey", st.TagClass);
    }

    [Fact]
    public void NullPublishFrom_NotCurrent_IsDraft_Grey()
    {
        var st = PageVersionStatus.Of(isCurrent: false, publishFrom: null, nowUtc: Now);

        Assert.Equal("Draft", st.Label);
        Assert.Equal("govuk-tag--grey", st.TagClass);
    }

    // Edge: isCurrent wins over a past publish window
    [Fact]
    public void Current_Wins_Over_PastWindow()
    {
        var st = PageVersionStatus.Of(isCurrent: true, publishFrom: Now.AddDays(-100), nowUtc: Now);

        Assert.Equal("Live", st.Label);
    }
}
