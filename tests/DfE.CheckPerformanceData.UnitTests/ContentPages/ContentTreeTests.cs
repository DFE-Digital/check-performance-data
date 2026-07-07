using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The content tree is the heart of the page-builder model: an ordered list of nodes that are
// either regions (layout containers with columns of child nodes, nestable) or widgets (a type
// plus free-form props). It persists as JSON, so faithful round-tripping is the contract.
public class ContentTreeTests
{
    [Fact]
    public void RegionsAndWidgets_RoundTrip_PreservesStructureAndNesting()
    {
        IReadOnlyList<ContentNode> tree =
        [
            new WidgetNode
            {
                Type = "heading",
                Anchor = "key-dates",
                Props = new JsonObject { ["level"] = 2, ["text"] = "Key dates" }
            },
            new RegionNode
            {
                Layout = RegionLayout.Thirds,
                Columns =
                [
                    [new WidgetNode { Type = "card", Props = new JsonObject { ["title"] = "A" } }],
                    [new WidgetNode { Type = "card", Props = new JsonObject { ["title"] = "B" } }],
                    [new RegionNode { Layout = RegionLayout.Halves, Columns = [[], []] }]
                ]
            }
        ];

        var json = ContentPageJson.Serialize(tree);
        var back = ContentPageJson.Deserialize(json);

        Assert.NotNull(back);
        Assert.Equal(2, back!.Count);

        var heading = Assert.IsType<WidgetNode>(back[0]);
        Assert.Equal("heading", heading.Type);
        Assert.Equal("key-dates", heading.Anchor);
        Assert.Equal(2, (int)heading.Props!["level"]!);
        Assert.Equal("Key dates", (string)heading.Props!["text"]!);

        var region = Assert.IsType<RegionNode>(back[1]);
        Assert.Equal(RegionLayout.Thirds, region.Layout);
        Assert.Equal(3, region.Columns.Count);
        Assert.Equal("card", Assert.IsType<WidgetNode>(region.Columns[0][0]).Type);

        var nested = Assert.IsType<RegionNode>(region.Columns[2][0]);
        Assert.Equal(RegionLayout.Halves, nested.Layout);
        Assert.Equal(2, nested.Columns.Count);
    }

    [Fact]
    public void Kind_Discriminator_IsWritten_ForBothNodeKinds()
    {
        IReadOnlyList<ContentNode> tree =
        [
            new RegionNode { Layout = RegionLayout.Single, Columns = [[]] },
            new WidgetNode { Type = "divider" }
        ];

        var json = ContentPageJson.Serialize(tree);

        Assert.Contains("\"kind\": \"region\"", json);
        Assert.Contains("\"kind\": \"widget\"", json);
        Assert.Contains("\"type\": \"divider\"", json);
    }

    [Theory]
    [InlineData(RegionLayout.Single, "single")]
    [InlineData(RegionLayout.Halves, "halves")]
    [InlineData(RegionLayout.Thirds, "thirds")]
    [InlineData(RegionLayout.Quarters, "quarters")]
    [InlineData(RegionLayout.OneThirdTwoThirds, "one-third-two-thirds")]
    [InlineData(RegionLayout.TwoThirdsOneThird, "two-thirds-one-third")]
    public void RegionLayout_Serialises_AsKebabCase(RegionLayout layout, string expected)
    {
        IReadOnlyList<ContentNode> tree = [new RegionNode { Layout = layout, Columns = [[]] }];

        var json = ContentPageJson.Serialize(tree);

        Assert.Contains($"\"layout\": \"{expected}\"", json);
    }

    [Fact]
    public void NullProps_And_NullAnchor_AreOmitted_FromJson()
    {
        IReadOnlyList<ContentNode> tree = [new WidgetNode { Type = "divider" }];

        var json = ContentPageJson.Serialize(tree);

        Assert.DoesNotContain("anchor", json);
        Assert.DoesNotContain("props", json);
    }
}
