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
    private static PageNodeVersionDto Version(int id, DateTime? from, DateTime? to = null) =>
        new() { VersionId = id, PublishFrom = from, PublishTo = to, MinorVersion = from is null ? 1 : 0, Content = "[]" };

    // A higher-numbered version on the same window goes live instead, so the older one never will.
    [Fact]
    public void AScheduledVersion_ReplacedByANewerOneOnTheSameWindow_IsReplaced_Grey()
    {
        var old = Version(2, Now.AddDays(7), Now.AddDays(14));
        var all = new List<PageNodeVersionDto> { Version(1, Now.AddDays(-1)), old, Version(3, Now.AddDays(7), Now.AddDays(14)) };

        var st = PageVersionStatus.Of(old, all, Now);

        Assert.Equal("Replaced", st.Label);
        Assert.Equal("govuk-tag--grey", st.TagClass);
        Assert.Equal("Scheduled", PageVersionStatus.Of(all[2], all, Now).Label);
    }

    [Theory]
    [InlineData(8, null)]   // a later start: the older version is live for a day first
    [InlineData(7, 10)]     // the newer one ends first, then the older version goes live
    public void AScheduledVersion_StaysScheduled_WhenItStillGoesLive(int newerFromDays, int? newerToDays)
    {
        var old = Version(2, Now.AddDays(7), Now.AddDays(14));
        var newer = Version(3, Now.AddDays(newerFromDays), newerToDays is { } t ? Now.AddDays(t) : null);

        Assert.Equal("Scheduled", PageVersionStatus.Of(old, [old, newer], Now).Label);
    }

    [Fact]
    public void TheListOverload_GivesTheSameStatusAsTheFlags_ForOtherVersions()
    {
        var live = new PageNodeVersionDto { VersionId = 1, PublishFrom = Now.AddDays(-1), IsCurrent = true, Content = "[]" };
        var draft = Version(2, null);

        Assert.Equal("Live", PageVersionStatus.Of(live, [live, draft], Now).Label);
        Assert.Equal("Draft", PageVersionStatus.Of(draft, [live, draft], Now).Label);
    }
}
