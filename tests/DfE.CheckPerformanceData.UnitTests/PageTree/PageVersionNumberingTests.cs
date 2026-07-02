using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// Pure unit tests for PageVersionNumbering label derivation.
// Whole-integer scheme: every version — draft or published — is labelled with a single
// integer. A draft is labelled with the integer it will become on publish (next major
// after the most recent live version), and that label does not shift as the draft is saved.
public sealed class PageVersionNumberingTests
{
    private static PageNodeVersionDto Draft(int versionId, int minor) => new()
    {
        Id = Guid.NewGuid(),
        VersionId = versionId,
        MinorVersion = minor,
        IsCurrent = false,
        PublishFrom = null,
        Content = string.Empty,
        CreatedDate = DateTime.UtcNow,
        UpdatedDate = DateTime.UtcNow
    };

    private static PageNodeVersionDto Published(int versionId) => new()
    {
        Id = Guid.NewGuid(),
        VersionId = versionId,
        MinorVersion = 0,
        IsCurrent = true,
        PublishFrom = DateTime.UtcNow.AddDays(-1),
        Content = string.Empty,
        CreatedDate = DateTime.UtcNow,
        UpdatedDate = DateTime.UtcNow
    };

    // The first draft on a brand-new page (nothing published yet) is "1" — it will
    // publish as version 1.
    [Fact]
    public void Label_FirstDraft_NothingPublished_Is_1()
    {
        var v = Draft(1, 1);
        var all = new[] { v };

        Assert.Equal("1", PageVersionNumbering.Label(all, v));
    }

    [Fact]
    public void Label_SinglePublishedVersion_Is_1()
    {
        var v = Published(1);
        var all = new[] { v };

        Assert.Equal("1", PageVersionNumbering.Label(all, v));
    }

    // Draft opened after v1 is published → "2".
    [Fact]
    public void Label_PublishedV1_PlusDraftV2_DraftIs_2()
    {
        var v1 = Published(1);
        var v2 = Draft(2, 2);
        var all = new[] { v2, v1 };

        Assert.Equal("1", PageVersionNumbering.Label(all, v1));
        Assert.Equal("2", PageVersionNumbering.Label(all, v2));
    }

    // Two publishes + a draft on top → drafts labelled "3", published still "1" and "2".
    [Fact]
    public void Label_TwoPublished_PlusDraft_DraftIs_3()
    {
        var v1 = Published(1);
        var v2 = Published(2);
        var v3 = Draft(3, 1);
        var all = new[] { v3, v2, v1 };

        Assert.Equal("1", PageVersionNumbering.Label(all, v1));
        Assert.Equal("2", PageVersionNumbering.Label(all, v2));
        Assert.Equal("3", PageVersionNumbering.Label(all, v3));
    }

    // The draft's label is stable — it does not shift as the editor saves it again
    // (which bumps MinorVersion in storage).
    [Fact]
    public void Label_DraftLabel_DoesNotShift_WhenMinorIncrements()
    {
        var v1 = Published(1);
        var drafted = Draft(2, 1);
        var draftedAgain = Draft(2, 7);

        var all1 = new[] { drafted, v1 };
        var all2 = new[] { draftedAgain, v1 };

        Assert.Equal("2", PageVersionNumbering.Label(all1, drafted));
        Assert.Equal("2", PageVersionNumbering.Label(all2, draftedAgain));
    }

    [Fact]
    public void MajorFor_PublishedVersion_CountsItself()
    {
        var v = Published(5);
        var all = new[] { v };

        Assert.Equal(1, PageVersionNumbering.MajorFor(all, v));
    }

    // A draft opened after two publishes → major = 3 (next integer).
    [Fact]
    public void MajorFor_DraftAfterTwoPublishes_IsNextInteger()
    {
        var v1 = Published(1);
        var v2 = Published(3);
        var v3 = Draft(5, 1);
        var all = new[] { v3, v2, v1 };

        Assert.Equal(3, PageVersionNumbering.MajorFor(all, v3));
    }
}
