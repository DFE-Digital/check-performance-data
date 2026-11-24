using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// A page version shows its own number. The number is given when the version is added and is
// unique for the page, so no two versions show the same number and no number changes later.
public sealed class PageVersionNumberingTests
{
    private static PageNodeVersionDto Draft(int versionId, int minor) => new()
    {
        Id = Guid.NewGuid(),
        VersionId = versionId,
        MinorVersion = minor,
        PublishFrom = null,
        Content = string.Empty
    };

    private static PageNodeVersionDto Published(int versionId) => new()
    {
        Id = Guid.NewGuid(),
        VersionId = versionId,
        MinorVersion = 0,
        IsCurrent = true,
        PublishFrom = DateTime.UtcNow.AddDays(-1),
        Content = string.Empty
    };

    private static PageNodeVersionDto Scheduled(int versionId) => new()
    {
        Id = Guid.NewGuid(),
        VersionId = versionId,
        MinorVersion = 0,
        PublishFrom = DateTime.UtcNow.AddDays(7),
        Content = string.Empty
    };

    private static List<string> Labels(params PageNodeVersionDto[] versions) =>
        versions.Select(PageVersionNumbering.Label).ToList();

    // A new page: the first draft is 1 and publishes as 1.
    [Fact]
    public void Label_TheFirstDraft_Is1_AndPublishesAs1()
    {
        Assert.Equal("1", PageVersionNumbering.Label(Draft(1, 1)));
        Assert.Equal("1", PageVersionNumbering.Label(Published(1)));
    }

    // Live 1 and a draft: the draft is 2, and it is still 2 when it is published.
    [Fact]
    public void Label_ADraftAboveTheLiveVersion_IsTheNumberItPublishesAs()
    {
        Assert.Equal(["1", "2"], Labels(Published(1), Draft(2, 1)));
        Assert.Equal(["1", "2"], Labels(Published(1), Published(2)));
    }

    // Saving a draft again does not change its number.
    [Fact]
    public void Label_DoesNotChange_WhenTheDraftIsSavedAgain()
    {
        Assert.Equal("2", PageVersionNumbering.Label(Draft(2, 1)));
        Assert.Equal("2", PageVersionNumbering.Label(Draft(2, 7)));
    }

    // An editor publishes 2, schedules 3, unpublishes 2 and saves it again. Version 2 is a draft
    // below the scheduled version 3, and the two must not share a number.
    [Fact]
    public void Label_ADraftBelowAScheduledVersion_HasItsOwnNumber()
    {
        Assert.Equal(["1", "2", "3"], Labels(Published(1), Draft(2, 1), Scheduled(3)));
    }

    // Moving embedded images adds a live version above the editor's draft, then a new draft above
    // that. Every version keeps its own number, and the editor's draft keeps the one it had.
    [Fact]
    public void Label_ALiveVersionAddedAboveADraft_HasTheNextFreeNumber()
    {
        Assert.Equal(["1", "2", "3", "4"], Labels(Published(1), Draft(2, 1), Published(3), Draft(4, 1)));
    }
}
