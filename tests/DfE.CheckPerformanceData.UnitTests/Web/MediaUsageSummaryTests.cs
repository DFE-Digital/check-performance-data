using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Where an image is used, as the image page and the delete page show it: pages whose current
// version uses it, and pages where only older versions (or a deleted page) still hold it.
public sealed class MediaUsageSummaryTests
{
    private static MediaUsagePage Page(string title, bool current, bool deleted = false) =>
        new(Guid.NewGuid(), title, title.ToLowerInvariant(), deleted, current);

    [Fact]
    public void NoUsages_IsNotUsed()
    {
        var summary = new MediaUsageSummary([]);

        Assert.False(summary.IsUsed);
        Assert.False(summary.OnlyInOlderVersions);
        Assert.Empty(summary.CurrentPages);
        Assert.Empty(summary.OlderPages);
        Assert.Equal("Not used on any page", summary.UsedOn);
    }

    [Fact]
    public void Pages_AreSplit_IntoCurrentAndOlder_AndADeletedPageIsOlder()
    {
        var live = Page("Attendance", current: true);
        var older = Page("Absence", current: false);
        var deleted = Page("Exclusions", current: true, deleted: true);

        var summary = new MediaUsageSummary([older, live, deleted]);

        Assert.Equal([live], summary.CurrentPages);
        Assert.Equal([older, deleted], summary.OlderPages);
        Assert.True(summary.IsUsed);
        Assert.False(summary.OnlyInOlderVersions);
    }

    [Theory]
    [InlineData(1, "1 page")]
    [InlineData(3, "3 pages")]
    public void UsedOn_CountsThePagesThatUseItNow(int count, string expected)
    {
        var pages = Enumerable.Range(0, count).Select(i => Page($"P{i}", current: true)).Append(Page("Old", current: false)).ToList();

        Assert.Equal(expected, new MediaUsageSummary(pages).UsedOn);
    }

    [Theory]
    [InlineData(1, "Only in older versions of 1 page")]
    [InlineData(2, "Only in older versions of 2 pages")]
    public void OnlyOlderVersions_IsSaid(int count, string expected)
    {
        var pages = Enumerable.Range(0, count).Select(i => Page($"P{i}", current: false)).ToList();

        var summary = new MediaUsageSummary(pages);

        Assert.True(summary.IsUsed);
        Assert.True(summary.OnlyInOlderVersions);
        Assert.Equal(expected, summary.UsedOn);
    }
}
