using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// After any edit, heading anchors are regenerated from heading text so the auto-nav and deep links
// stay correct and unique — editors never set anchors by hand. Headings anywhere in the tree (incl.
// nested regions) are anchored in document order; duplicates get a numeric suffix.
public class HeadingAnchorizerTests
{
    private static WidgetNode Heading(string text) => new()
    {
        Type = "heading",
        Props = new JsonObject { ["level"] = 2, ["text"] = text }
    };

    [Fact]
    public void Anchors_HeadingsFromTheirText()
    {
        IReadOnlyList<ContentNode> tree = [Heading("Key dates")];

        HeadingAnchorizer.Apply(tree);

        Assert.Equal("key-dates", ((WidgetNode)tree[0]).Anchor);
    }

    [Fact]
    public void Anchors_AreMadeUnique_WithNumericSuffix()
    {
        IReadOnlyList<ContentNode> tree = [Heading("Get support"), Heading("Get support")];

        HeadingAnchorizer.Apply(tree);

        Assert.Equal("get-support", ((WidgetNode)tree[0]).Anchor);
        Assert.Equal("get-support-2", ((WidgetNode)tree[1]).Anchor);
    }

    [Fact]
    public void Anchors_HeadingsNestedInsideRegions()
    {
        var heading = Heading("Overview");
        IReadOnlyList<ContentNode> tree =
        [
            new RegionNode { Layout = RegionLayout.Single, Columns = [[heading]] }
        ];

        HeadingAnchorizer.Apply(tree);

        Assert.Equal("overview", heading.Anchor);
    }

    [Fact]
    public void Leaves_NonHeadingWidgetsAlone()
    {
        var richtext = new WidgetNode { Type = "richtext", Props = new JsonObject { ["html"] = "<p>x</p>" } };
        IReadOnlyList<ContentNode> tree = [richtext];

        HeadingAnchorizer.Apply(tree);

        Assert.Null(richtext.Anchor);
    }
}
