using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// Pure unit tests for PageVersionNumbering label derivation.
// No DB, no Razor — just list manipulation.
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

    // One draft (VersionId=1, Minor=1), nothing published → label "0.01"
    [Fact]
    public void Label_OneDraft_NothingPublished_Returns_0_01()
    {
        var v = Draft(1, 1);
        var all = new[] { v };

        var label = PageVersionNumbering.Label(all, v);

        Assert.Equal("0.01", label);
    }

    // After publish (VersionId=1, Minor=0) → label "1"
    [Fact]
    public void Label_SinglePublishedVersion_Returns_1()
    {
        var v = Published(1);
        var all = new[] { v };

        var label = PageVersionNumbering.Label(all, v);

        Assert.Equal("1", label);
    }

    // published v1 (Minor=0) + draft v2 (Minor=2) → v1="1", v2="1.02"
    [Fact]
    public void Label_PublishedV1_PlusDraftV2_Minor2()
    {
        var v1 = Published(1);
        var v2 = Draft(2, 2);
        var all = new[] { v2, v1 }; // newest first

        Assert.Equal("1", PageVersionNumbering.Label(all, v1));
        Assert.Equal("1.02", PageVersionNumbering.Label(all, v2));
    }

    // published v1 (Minor=0) + published v2 (Minor=0) + draft v3 (Minor=1)
    // → v1="1", v2="2", v3="2.01"
    [Fact]
    public void Label_TwoPublished_PlusDraftV3_Minor1()
    {
        var v1 = Published(1);
        var v2 = Published(2);
        var v3 = Draft(3, 1);
        var all = new[] { v3, v2, v1 }; // newest first

        Assert.Equal("1", PageVersionNumbering.Label(all, v1));
        Assert.Equal("2", PageVersionNumbering.Label(all, v2));
        Assert.Equal("2.01", PageVersionNumbering.Label(all, v3));
    }

    // MajorFor: integer version counts itself in its own rank
    [Fact]
    public void MajorFor_PublishedVersion_CountsItself()
    {
        var v = Published(5);
        var all = new[] { v };

        Assert.Equal(1, PageVersionNumbering.MajorFor(all, v));
    }

    // MajorFor: draft version counts published versions with VersionId < own
    [Fact]
    public void MajorFor_DraftVersion_CountsPublishedBeforeIt()
    {
        var v1 = Published(1);
        var v2 = Published(3);
        var v3 = Draft(5, 1);
        var all = new[] { v3, v2, v1 };

        // Two published versions (1 and 3) both have VersionId < 5 → major = 2
        Assert.Equal(2, PageVersionNumbering.MajorFor(all, v3));
    }

    // Two-digit minor is zero-padded to exactly 2 digits
    [Fact]
    public void Label_DraftWithMinor10_PadsTwoDigits()
    {
        var v1 = Published(1);
        var v2 = Draft(2, 10);
        var all = new[] { v2, v1 };

        // major=1, minor=10 → "1.10"
        Assert.Equal("1.10", PageVersionNumbering.Label(all, v2));
    }
}
