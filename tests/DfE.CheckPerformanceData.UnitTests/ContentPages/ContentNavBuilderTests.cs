using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The left-hand nav is built automatically by walking the content tree for Heading widgets, in
// document order: H2 → top-level item, H3 → nested under the most recent H2, H4 → nested under
// the most recent H3. Editors never hand maintain it. Headings nested inside regions are found
// just the same.
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
    public void NonHeadingWidgets_AndLevelsOutsideH2ToH4_AreIgnored()
    {
        // H1 is the page title and would duplicate the heading above the nav; H5 and H6 are
        // below the depth a contents list stays readable at.
        IReadOnlyList<ContentNode> tree =
        [
            Heading(1, "Page title", "page-title"),
            new WidgetNode { Type = "divider" },
            Heading(2, "Real section", "real-section"),
            Heading(5, "Too deep", "too-deep"),
            Heading(6, "Deeper still", "deeper-still")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Single(nav);
        Assert.Equal("real-section", Trim(nav[0].Href));
        Assert.Empty(nav[0].Children);
    }

    [Fact]
    public void H4s_NestUnderThePrecedingH3()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Removing a pupil", "removing-a-pupil"),
            Heading(3, "Reasons for removal", "reasons-for-removal"),
            Heading(4, "Admitted following permanent exclusion", "admitted-exclusion"),
            Heading(4, "Admitted from abroad", "admitted-abroad"),
            Heading(3, "Evidence", "evidence")
        ];

        var nav = ContentNavBuilder.Build(tree);

        var section = Assert.Single(nav);
        Assert.Equal(2, section.Children.Count);

        var reasons = section.Children[0];
        Assert.Equal("reasons-for-removal", Trim(reasons.Href));
        Assert.Equal(
            ["admitted-exclusion", "admitted-abroad"],
            reasons.Children.Select(c => Trim(c.Href)));

        // The following H3 starts a fresh branch rather than collecting the previous H4s.
        Assert.Empty(section.Children[1].Children);
    }

    [Fact]
    public void AnH4WithNoPrecedingH3_NestsUnderTheH2()
    {
        // Authors skip levels. Dropping the heading would leave a section the nav cannot reach,
        // so it attaches at the nearest level that exists.
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Removing a pupil", "removing-a-pupil"),
            Heading(4, "Admitted following permanent exclusion", "admitted-exclusion")
        ];

        var nav = ContentNavBuilder.Build(tree);

        var section = Assert.Single(nav);
        Assert.Equal("admitted-exclusion", Trim(Assert.Single(section.Children).Href));
    }

    [Fact]
    public void AnH4BeforeAnyH2OrH3_IsTopLevel()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(4, "Orphan", "orphan"),
            Heading(2, "Real section", "real-section")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Equal(["orphan", "real-section"], nav.Select(n => Trim(n.Href)));
    }

    [Fact]
    public void AnH3AfterAnH4_ReturnsToTheH3Level()
    {
        // The walk has to track the current H3 and reset it, or a later H3 would end up nested
        // inside the previous H3's children.
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Section", "section"),
            Heading(3, "First", "first"),
            Heading(4, "Detail", "detail"),
            Heading(3, "Second", "second")
        ];

        var nav = ContentNavBuilder.Build(tree);

        var section = Assert.Single(nav);
        Assert.Equal(["first", "second"], section.Children.Select(c => Trim(c.Href)));
    }

    [Fact]
    public void ANewH2_ResetsTheH3Branch()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "First section", "first-section"),
            Heading(3, "Sub", "sub"),
            Heading(2, "Second section", "second-section"),
            Heading(4, "Detail", "detail")
        ];

        var nav = ContentNavBuilder.Build(tree);

        Assert.Equal(2, nav.Count);
        // The H4 belongs to the second section, not to the first section's H3.
        Assert.Empty(nav[0].Children[0].Children);
        Assert.Equal("detail", Trim(Assert.Single(nav[1].Children).Href));
    }

    private static string Trim(string href) => href.TrimStart('#');
}
