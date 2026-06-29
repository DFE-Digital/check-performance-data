using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The left-hand nav is built automatically by walking the content tree for Heading widgets, in
// document order: H2 → top-level item, H3 → nested under the most recent H2. Editors never hand
// maintain it. Headings nested inside regions are found just the same.
public class ContentNavBuilderTests
{
    private static WidgetNode Heading(int level, string text, string anchor) => new()
    {
        Type = "heading",
        Anchor = anchor,
        Props = new JsonObject { ["level"] = level, ["text"] = text }
    };

    [Fact]
    public void FlatH2s_BecomeTopLevelItems_InOrder_WithAnchorHrefs()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Key dates", "key-dates"),
            new WidgetNode { Type = "richtext", Props = new JsonObject { ["html"] = "<p>x</p>" } },
            Heading(2, "Get support", "get-support")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Equal(2, nav.Count);
        Assert.Equal("Key dates", nav[0].Text);
        Assert.Equal("#key-dates", nav[0].Href);
        Assert.Empty(nav[0].Children);
        Assert.Equal("Get support", nav[1].Text);
        Assert.Equal("#get-support", nav[1].Href);
    }

    [Fact]
    public void H3s_NestUnderThePrecedingH2()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Pupil removal reason", "pupil-removal-reason"),
            Heading(3, "Child has died", "child-has-died"),
            Heading(3, "Elective home education", "elective-home-education"),
            Heading(2, "Get support", "get-support")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Equal(2, nav.Count);
        Assert.Equal("pupil-removal-reason", Trim(nav[0].Href));
        Assert.Equal(2, nav[0].Children.Count);
        Assert.Equal("Child has died", nav[0].Children[0].Text);
        Assert.Equal("#elective-home-education", nav[0].Children[1].Href);
        Assert.Empty(nav[1].Children);
    }

    [Fact]
    public void HeadingsInsideNestedRegions_AreFound_InDocumentOrder()
    {
        IReadOnlyList<ContentNode> tree =
        [
            new RegionNode
            {
                Layout = RegionLayout.TwoThirdsOneThird,
                Columns =
                [
                    [Heading(2, "Overview", "overview")],
                    [new RegionNode { Layout = RegionLayout.Single, Columns = [[Heading(2, "Aside", "aside")]] }]
                ]
            }
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Equal(["overview", "aside"], nav.Select(n => Trim(n.Href)));
    }

    [Fact]
    public void NonHeadingWidgets_AndNonH2H3Levels_AreIgnored()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(1, "Page title", "page-title"),
            new WidgetNode { Type = "divider" },
            Heading(2, "Real section", "real-section"),
            Heading(4, "Too deep", "too-deep")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Single(nav);
        Assert.Equal("real-section", Trim(nav[0].Href));
    }

    private static string Trim(string href) => href.TrimStart('#');
}
