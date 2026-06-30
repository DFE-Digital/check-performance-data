using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The inline editor mutates a content tree by addressing a position with a path of steps. Each
// step is (Column, Index): at the root the column is always 0 (the root list is a single implicit
// column); descending into a region, Column picks the region's column and Index the slot within it.
// These pure operations back the add / move-up / move-down / delete / nest editor actions.
public class ContentTreeEditorTests
{
    private static WidgetNode W(string id) => new() { Type = "widget", Anchor = id };

    private static string? Id(ContentNode node) => ((WidgetNode)node).Anchor;

    private static IEnumerable<string?> Ids(IEnumerable<ContentNode> nodes) => nodes.Select(Id);

    [Fact]
    public void InsertAt_IndexEqualsCount_AppendsToRoot()
    {
        var root = new List<ContentNode> { W("a"), W("b") };

        ContentTreeEditor.InsertAt(root, [new TreeStep(0, 2)], W("c"));

        Assert.Equal(["a", "b", "c"], Ids(root));
    }

    [Fact]
    public void InsertAt_InsertsBeforeExistingNode()
    {
        var root = new List<ContentNode> { W("a"), W("b") };

        ContentTreeEditor.InsertAt(root, [new TreeStep(0, 1)], W("x"));

        Assert.Equal(["a", "x", "b"], Ids(root));
    }

    [Fact]
    public void Remove_TopLevelNode()
    {
        var root = new List<ContentNode> { W("a"), W("b"), W("c") };

        ContentTreeEditor.Remove(root, [new TreeStep(0, 1)]);

        Assert.Equal(["a", "c"], Ids(root));
    }

    [Fact]
    public void MoveUp_SwapsWithPreviousSibling()
    {
        var root = new List<ContentNode> { W("a"), W("b"), W("c") };

        ContentTreeEditor.MoveUp(root, [new TreeStep(0, 2)]);

        Assert.Equal(["a", "c", "b"], Ids(root));
    }

    [Fact]
    public void MoveUp_AtFirstPosition_IsNoOp()
    {
        var root = new List<ContentNode> { W("a"), W("b") };

        ContentTreeEditor.MoveUp(root, [new TreeStep(0, 0)]);

        Assert.Equal(["a", "b"], Ids(root));
    }

    [Fact]
    public void MoveDown_SwapsWithNextSibling()
    {
        var root = new List<ContentNode> { W("a"), W("b"), W("c") };

        ContentTreeEditor.MoveDown(root, [new TreeStep(0, 0)]);

        Assert.Equal(["b", "a", "c"], Ids(root));
    }

    [Fact]
    public void MoveDown_AtLastPosition_IsNoOp()
    {
        var root = new List<ContentNode> { W("a"), W("b") };

        ContentTreeEditor.MoveDown(root, [new TreeStep(0, 1)]);

        Assert.Equal(["a", "b"], Ids(root));
    }

    [Fact]
    public void InsertAt_NestsIntoRegionColumn()
    {
        var region = new RegionNode
        {
            Layout = RegionLayout.Halves,
            Columns = [[W("a")], [W("b")]]
        };
        var root = new List<ContentNode> { region };

        // Root index 0 → the region; then column 1, position 1 → append after "b".
        ContentTreeEditor.InsertAt(root, [new TreeStep(0, 0), new TreeStep(1, 1)], W("c"));

        Assert.Equal(["b", "c"], Ids(region.Columns[1]));
        Assert.Equal(["a"], Ids(region.Columns[0]));
    }

    [Fact]
    public void Remove_NestedNode()
    {
        var region = new RegionNode
        {
            Layout = RegionLayout.Halves,
            Columns = [[W("a")], [W("b"), W("c")]]
        };
        var root = new List<ContentNode> { region };

        ContentTreeEditor.Remove(root, [new TreeStep(0, 0), new TreeStep(1, 0)]);

        Assert.Equal(["c"], Ids(region.Columns[1]));
    }

    [Fact]
    public void MoveDown_NestedNode()
    {
        var region = new RegionNode
        {
            Layout = RegionLayout.Halves,
            Columns = [[W("a")], [W("b"), W("c")]]
        };
        var root = new List<ContentNode> { region };

        ContentTreeEditor.MoveDown(root, [new TreeStep(0, 0), new TreeStep(1, 0)]);

        Assert.Equal(["c", "b"], Ids(region.Columns[1]));
    }

    [Fact]
    public void Remove_ReturnsTheRemovedNode()
    {
        var root = new List<ContentNode> { W("a"), W("b") };

        var removed = ContentTreeEditor.Remove(root, [new TreeStep(0, 1)]);

        Assert.Equal("b", Id(removed));
    }
}
