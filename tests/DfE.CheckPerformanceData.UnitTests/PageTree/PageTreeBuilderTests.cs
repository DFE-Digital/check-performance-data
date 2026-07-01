using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public sealed class PageTreeBuilderTests
{
    // ---------- helper ----------

    private static PageNodeTreeItemDto Row(Guid id, Guid? parentId, string title, int sortOrder = 0) =>
        new()
        {
            Id = id,
            ParentId = parentId,
            Segment = title.ToLowerInvariant().Replace(" ", "-"),
            Path = "/" + title.ToLowerInvariant().Replace(" ", "-"),
            SortOrder = sortOrder,
            Title = title,
            PageType = "Standard",
            HasLiveVersion = false,
        };

    // ---------- empty input ----------

    [Fact]
    public void Build_EmptyInput_ReturnsEmptyList()
    {
        var result = PageTreeBuilder.Build(Array.Empty<PageNodeTreeItemDto>());
        Assert.Empty(result);
    }

    // ---------- core nesting + ordering (rows deliberately out of order) ----------

    [Fact]
    public void Build_FlatRows_ProducesCorrectlyNestedAndOrderedTree()
    {
        var rootId       = Guid.NewGuid();
        var childAId     = Guid.NewGuid();
        var childBId     = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        // Fed in reverse/scrambled SortOrder to prove sorting,
        // and grandchild before its parent to prove parent-after-child works.
        var rows = new[]
        {
            Row(grandchildId, childAId, "Grandchild", sortOrder: 0),  // grandchild first
            Row(childBId,     rootId,   "Child B",    sortOrder: 2),  // higher sibling sort
            Row(childAId,     rootId,   "Child A",    sortOrder: 1),  // lower sibling sort, out of input order
            Row(rootId,       null,     "Root",       sortOrder: 0),  // root last
        };

        var result = PageTreeBuilder.Build(rows);

        // single root
        Assert.Single(result);
        var root = result[0];
        Assert.Equal("Root", root.Title);

        // two children, sorted by SortOrder
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Child A", root.Children[0].Title);
        Assert.Equal("Child B", root.Children[1].Title);

        // Child A carries the grandchild
        Assert.Single(root.Children[0].Children);
        Assert.Equal("Grandchild", root.Children[0].Children[0].Title);

        // Child B has no children
        Assert.Empty(root.Children[1].Children);
    }

    // ---------- multiple roots, ordering ----------

    [Fact]
    public void Build_MultipleRoots_OrderedBySortOrder()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        // Feed in descending SortOrder to prove ascending sort
        var rows = new[]
        {
            Row(id3, null, "Third",  sortOrder: 3),
            Row(id1, null, "First",  sortOrder: 1),
            Row(id2, null, "Second", sortOrder: 2),
        };

        var result = PageTreeBuilder.Build(rows);

        Assert.Equal(3, result.Count);
        Assert.Equal("First",  result[0].Title);
        Assert.Equal("Second", result[1].Title);
        Assert.Equal("Third",  result[2].Title);
    }
}
