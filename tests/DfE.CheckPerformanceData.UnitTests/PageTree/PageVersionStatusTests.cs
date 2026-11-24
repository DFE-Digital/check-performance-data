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

    private static PageNodeVersionDto Replaced(int id, DateTime? from, DateTime? to = null) =>
        new() { VersionId = id, PublishFrom = from, PublishTo = to, Content = "[]", SupersededById = Guid.NewGuid() };

    // A version that another version replaced shows "Replaced", whatever its window says.
    [Theory]
    [InlineData(7)]      // it was scheduled
    [InlineData(-1)]     // it was live
    [InlineData(null)]   // it was the draft
    public void AReplacedVersion_IsReplaced_Grey(int? fromDays)
    {
        var old = Replaced(2, fromDays is { } d ? Now.AddDays(d) : null);

        var st = PageVersionStatus.Of(old, Now);

        Assert.Equal("Replaced", st.Label);
        Assert.Equal("govuk-tag--grey", st.TagClass);
    }

    // Replacement is recorded, not guessed: a scheduled version that nothing marked as replaced
    // stays scheduled, even when a newer version has the same window.
    [Fact]
    public void AScheduledVersion_StaysScheduled_UnlessItIsMarkedReplaced()
    {
        var old = Version(2, Now.AddDays(7), Now.AddDays(14));

        Assert.Equal("Scheduled", PageVersionStatus.Of(old, Now).Label);
    }

    [Fact]
    public void TheVersionOverload_GivesTheSameStatusAsTheFlags_ForOtherVersions()
    {
        var live = new PageNodeVersionDto { VersionId = 1, PublishFrom = Now.AddDays(-1), IsCurrent = true, Content = "[]" };
        var draft = Version(2, null);

        Assert.Equal("Live", PageVersionStatus.Of(live, Now).Label);
        Assert.Equal("Draft", PageVersionStatus.Of(draft, Now).Label);
    }
}
