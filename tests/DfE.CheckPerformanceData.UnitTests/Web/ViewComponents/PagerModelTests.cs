using DfE.CheckPerformanceData.Web.ViewComponents.Pager;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.ViewComponents;

// Locks down the truncation algorithm for the reusable admin pager. Every case here maps
// 1:1 to a scenario in the acceptance so a future regression tells you exactly which
// shape it broke. The algorithm produces a flat ordered list of PagerItem values —
// Previous / Page(n) / Ellipsis / Page(n) / ... / Next — with dedupe when the middle
// window overlaps either edge range.
public sealed class PagerModelTests
{
    [Fact]
    public void SinglePage_ReturnsEmptyList()
    {
        // A pager on a single-page table is noise — the caller shouldn't render anything.
        var items = PagerModel.BuildItems(currentPage: 1, totalPages: 1);
        Assert.Empty(items);
    }

    [Fact]
    public void ThreePagesOnFirst_RendersAllPagesNoEllipsisPrevDisabled()
    {
        // Below the truncation threshold: every page renders, no ellipsis, Prev disabled.
        var items = PagerModel.BuildItems(currentPage: 1, totalPages: 3);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: true, target: null),
            i => AssertPage(i, page: 1, current: true),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            i => AssertNext(i, disabled: false, target: 2));
    }

    [Fact]
    public void TenPagesOnFirst_RendersFirstEdgeEllipsisLastEdge()
    {
        // Boundary: 10 pages triggers truncation with the default edge=3 / neighbours=1.
        // Current lives in the first edge, so no separate middle block appears.
        var items = PagerModel.BuildItems(currentPage: 1, totalPages: 10);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: true, target: null),
            i => AssertPage(i, page: 1, current: true),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 8, current: false),
            i => AssertPage(i, page: 9, current: false),
            i => AssertPage(i, page: 10, current: false),
            i => AssertNext(i, disabled: false, target: 2));
    }

    [Fact]
    public void MidRangeOnThirtyFour_RendersFirstMiddleAndLastBlocksSeparatedByEllipses()
    {
        // The exact case Lance flagged: 34 buckets on the drill-in, current in the middle.
        // Three blocks separated by two ellipses.
        var items = PagerModel.BuildItems(currentPage: 5, totalPages: 34);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 4),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 4, current: false),
            i => AssertPage(i, page: 5, current: true),
            i => AssertPage(i, page: 6, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 32, current: false),
            i => AssertPage(i, page: 33, current: false),
            i => AssertPage(i, page: 34, current: false),
            i => AssertNext(i, disabled: false, target: 6));
    }

    [Fact]
    public void LastPageOnThirtyFour_RendersFirstEdgeEllipsisLastEdgeNextDisabled()
    {
        // Current lives in the last edge — no separate middle block; Next disabled.
        var items = PagerModel.BuildItems(currentPage: 34, totalPages: 34);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 33),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 32, current: false),
            i => AssertPage(i, page: 33, current: false),
            i => AssertPage(i, page: 34, current: true),
            i => AssertNext(i, disabled: true, target: null));
    }

    [Fact]
    public void SeventeenOnThirtyFour_RendersDeepMiddleBlockBetweenBothEdgeBlocks()
    {
        // Current in the deep middle — three fully-separated blocks with symmetric context.
        var items = PagerModel.BuildItems(currentPage: 17, totalPages: 34);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 16),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 16, current: false),
            i => AssertPage(i, page: 17, current: true),
            i => AssertPage(i, page: 18, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 32, current: false),
            i => AssertPage(i, page: 33, current: false),
            i => AssertPage(i, page: 34, current: false),
            i => AssertNext(i, disabled: false, target: 18));
    }

    [Fact]
    public void CurrentOverlappingFirstEdge_DedupesInsteadOfSplitting()
    {
        // Lance's dedupe case: current=4, edge=3, neighbours=1 → first-range [1,2,3] and
        // middle-range [3,4,5] overlap by 3. The overlap merges into a single continuous
        // block instead of rendering 3 twice with an ellipsis between them.
        var items = PagerModel.BuildItems(currentPage: 4, totalPages: 34);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 3),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            i => AssertPage(i, page: 4, current: true),
            i => AssertPage(i, page: 5, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 32, current: false),
            i => AssertPage(i, page: 33, current: false),
            i => AssertPage(i, page: 34, current: false),
            i => AssertNext(i, disabled: false, target: 5));
    }

    [Fact]
    public void CurrentOverlappingLastEdge_DedupesInsteadOfSplitting()
    {
        // Symmetric to the first-edge overlap case — the middle range shares a page with
        // the last-edge range and the two merge.
        var items = PagerModel.BuildItems(currentPage: 32, totalPages: 34);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 31),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 31, current: false),
            i => AssertPage(i, page: 32, current: true),
            i => AssertPage(i, page: 33, current: false),
            i => AssertPage(i, page: 34, current: false),
            i => AssertNext(i, disabled: false, target: 33));
    }

    [Fact]
    public void CustomEdgeAndNeighbourCounts_HonouredByAlgorithm()
    {
        // Callers can override edgeCount/visibleNeighbours per surface. edge=2 + neighbours=2
        // → first [1,2], middle [8,9,10,11,12], last [49,50], Prev/Next either side.
        var items = PagerModel.BuildItems(
            currentPage: 10, totalPages: 50, edgeCount: 2, visibleNeighbours: 2);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 9),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 8, current: false),
            i => AssertPage(i, page: 9, current: false),
            i => AssertPage(i, page: 10, current: true),
            i => AssertPage(i, page: 11, current: false),
            i => AssertPage(i, page: 12, current: false),
            AssertEllipsis,
            i => AssertPage(i, page: 49, current: false),
            i => AssertPage(i, page: 50, current: false),
            i => AssertNext(i, disabled: false, target: 11));
    }

    [Fact]
    public void ClampsCurrentPageAboveTotalPages()
    {
        // Defensive: a stale ?page=99 query on a shrunken corpus lands on the last page
        // instead of throwing or rendering an empty list.
        var items = PagerModel.BuildItems(currentPage: 99, totalPages: 5);

        Assert.Collection(items,
            i => AssertPrevious(i, disabled: false, target: 4),
            i => AssertPage(i, page: 1, current: false),
            i => AssertPage(i, page: 2, current: false),
            i => AssertPage(i, page: 3, current: false),
            i => AssertPage(i, page: 4, current: false),
            i => AssertPage(i, page: 5, current: true),
            i => AssertNext(i, disabled: true, target: null));
    }

    private static void AssertPrevious(PagerItem item, bool disabled, int? target)
    {
        Assert.Equal(PagerItemKind.Previous, item.Kind);
        Assert.Equal(disabled, item.IsDisabled);
        Assert.Equal(target, item.PageNumber);
        Assert.False(item.IsCurrent);
    }

    private static void AssertNext(PagerItem item, bool disabled, int? target)
    {
        Assert.Equal(PagerItemKind.Next, item.Kind);
        Assert.Equal(disabled, item.IsDisabled);
        Assert.Equal(target, item.PageNumber);
        Assert.False(item.IsCurrent);
    }

    private static void AssertPage(PagerItem item, int page, bool current)
    {
        Assert.Equal(PagerItemKind.Page, item.Kind);
        Assert.Equal(page, item.PageNumber);
        Assert.Equal(current, item.IsCurrent);
        Assert.False(item.IsDisabled);
    }

    private static void AssertEllipsis(PagerItem item)
    {
        Assert.Equal(PagerItemKind.Ellipsis, item.Kind);
        Assert.Null(item.PageNumber);
        Assert.False(item.IsCurrent);
        Assert.False(item.IsDisabled);
    }
}
