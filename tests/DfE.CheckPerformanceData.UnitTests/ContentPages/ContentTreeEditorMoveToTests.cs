using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Unit tests for ContentTreeEditor.MoveTo, covering the five cases from the spec:
// upward reorder in the same column, downward reorder with adjustment, cross-region widget
// migration, top-level region reorder, and the own-subtree safe-restore guard.
public class ContentTreeEditorMoveToTests
{
    private static WidgetNode W(string id) => new() { Type = "widget", Anchor = id };

    private static string? Id(ContentNode node) => ((WidgetNode)node).Anchor;

    private static IEnumerable<string?> Ids(IEnumerable<ContentNode> nodes) => nodes.Select(Id);

    // ── same column, upward ───────────────────────────────────────────────────

    [Fact]
    public void MoveTo_SameColumn_Upward_InsertsBeforeTarget()
    {
        // [A,B,C] move C (idx 2) before A (idx 0) → [C,A,B]
        // fromIndex 2 > toIndex 0 → no adjustment. Remove C → [A,B]. Insert at 0 → [C,A,B].
        var root = new List<ContentNode> { W("A"), W("B"), W("C") };

        ContentTreeEditor.MoveTo(root, [new TreeStep(0, 2)], [new TreeStep(0, 0)]);

        Assert.Equal(["C", "A", "B"], Ids(root));
    }

    // ── same column, downward with adjustment ────────────────────────────────

    [Fact]
    public void MoveTo_SameColumn_Downward_AdjustmentApplied_InsertBeforeC()
    {
        // [A,B,C] move A (idx 0) to just before C (idx 2 in the original list).
        // AdjustAfterRemoval: fromIdx 0 < toIdx 2 → toIdx becomes 1.
        // After removing A → [B,C]. Insert at 1 → [B,A,C]. A lands immediately before C.
        var root = new List<ContentNode> { W("A"), W("B"), W("C") };

        ContentTreeEditor.MoveTo(root, [new TreeStep(0, 0)], [new TreeStep(0, 2)]);

        Assert.Equal(["B", "A", "C"], Ids(root));
    }

    [Fact]
    public void MoveTo_SameColumn_Downward_AppendPosition_MovesToEnd()
    {
        // [A,B,C] move A (idx 0) to the append position (idx 3 in the 3-element list).
        // AdjustAfterRemoval: fromIdx 0 < toIdx 3 → toIdx becomes 2.
        // After removing A → [B,C]. Insert at 2 (= append) → [B,C,A].
        var root = new List<ContentNode> { W("A"), W("B"), W("C") };

        ContentTreeEditor.MoveTo(root, [new TreeStep(0, 0)], [new TreeStep(0, 3)]);

        Assert.Equal(["B", "C", "A"], Ids(root));
    }

    // ── cross-region column move ─────────────────────────────────────────────

    [Fact]
    public void MoveTo_CrossRegion_MovesWidgetFromSourceColumnToTargetColumn()
    {
        // r1.col0=[a], r1.col1=[b];  r2.col0=[c], r2.col1=[d]
        // Move b (r1.col1[0]) to r2.col0 before c (r2.col0[0]).
        // Different parent steps → AdjustAfterRemoval does NOT fire.
        // After removing b → r1.col1=[]. InsertAt r2.col0[0] → r2.col0=[b,c].
        var r1 = new RegionNode { Layout = RegionLayout.Halves, Columns = [[W("a")], [W("b")]] };
        var r2 = new RegionNode { Layout = RegionLayout.Halves, Columns = [[W("c")], [W("d")]] };
        var root = new List<ContentNode> { r1, r2 };

        ContentTreeEditor.MoveTo(root,
            [new TreeStep(0, 0), new TreeStep(1, 0)],  // r1.col1[0] = b
            [new TreeStep(0, 1), new TreeStep(0, 0)]); // r2.col0[0] = before c

        Assert.Empty(r1.Columns[1]);
        Assert.Equal(["b", "c"], Ids(r2.Columns[0]));
    }

    // ── top-level region reorder ─────────────────────────────────────────────

    [Fact]
    public void MoveTo_TopLevelRegion_ToEarlierIndex_ReordersCorrectly()
    {
        // Use different layouts as proxy identifiers: [Single, Halves, Thirds]
        // Move Thirds (idx 2) before Halves (idx 1) → [Single, Thirds, Halves]
        var rA = new RegionNode { Layout = RegionLayout.Single,  Columns = [[]] };
        var rB = new RegionNode { Layout = RegionLayout.Halves,  Columns = [[]] };
        var rC = new RegionNode { Layout = RegionLayout.Thirds,  Columns = [[]] };
        var root = new List<ContentNode> { rA, rB, rC };

        ContentTreeEditor.MoveTo(root, [new TreeStep(0, 2)], [new TreeStep(0, 1)]);

        var layouts = root.Cast<RegionNode>().Select(r => r.Layout).ToList();
        Assert.Equal([RegionLayout.Single, RegionLayout.Thirds, RegionLayout.Halves], layouts);
    }

    // ── own-subtree safe-restore ─────────────────────────────────────────────

    [Fact]
    public void MoveTo_OwnSubtree_Restores_NodeCountUnchanged_NoException()
    {
        // Dropping a region onto a path inside its own subtree is invalid.
        // The try/catch in MoveTo must restore the region at its original index.
        var region = new RegionNode { Layout = RegionLayout.Single, Columns = [[W("x")]] };
        var root = new List<ContentNode> { region };

        // fromPath = root[0] (the region itself)
        // toPath = root[0].col0[0] (inside the region)
        ContentTreeEditor.MoveTo(root,
            [new TreeStep(0, 0)],
            [new TreeStep(0, 0), new TreeStep(0, 0)]);

        Assert.Single(root);
        Assert.Same(region, root[0]);
    }
}
