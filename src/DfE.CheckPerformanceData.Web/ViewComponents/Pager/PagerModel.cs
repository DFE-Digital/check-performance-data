namespace DfE.CheckPerformanceData.Web.ViewComponents.Pager;

// Truncation algorithm for the admin pager. Pure structural logic — takes the current
// position + total pages and returns the flat ordered list of items the Razor partial
// renders (Prev / edge / ellipsis / middle / ellipsis / edge / Next). The urlBuilder is
// applied at render time in the view component, so this class stays free of HTTP
// concerns and can be unit-tested in isolation.
//
// The algorithm produces three logical blocks:
//   * first-edge  = [1 .. edgeCount]
//   * middle      = [currentPage - visibleNeighbours .. currentPage + visibleNeighbours]
//   * last-edge   = [totalPages - edgeCount + 1 .. totalPages]
//
// If the middle range shares any page with an edge range, the two are merged into a
// single continuous block (dedupe). If the merged first and last blocks overlap or
// touch (adjacent), everything collapses into one range and the pager renders all pages
// with no ellipsis. Otherwise an ellipsis appears between each pair of separate blocks —
// even when the blocks are visually adjacent (middle_start = first_end + 1) — because
// the ellipsis marks the block boundary, not the specific number of skipped pages.
public static class PagerModel
{
    public const int DefaultEdgeCount = 3;
    public const int DefaultVisibleNeighbours = 1;

    public static IReadOnlyList<PagerItem> BuildItems(
        int currentPage,
        int totalPages,
        int edgeCount = DefaultEdgeCount,
        int visibleNeighbours = DefaultVisibleNeighbours)
    {
        if (totalPages <= 1)
        {
            return Array.Empty<PagerItem>();
        }

        // Clamp the input so a stale `?page=99` query on a shrunken corpus renders as the
        // last page rather than a nonsense pager. Also guard against negative edge/neighbour
        // arguments that would otherwise produce empty ranges and a Prev/Next-only pager.
        if (currentPage < 1) currentPage = 1;
        if (currentPage > totalPages) currentPage = totalPages;
        if (edgeCount < 1) edgeCount = 1;
        if (visibleNeighbours < 0) visibleNeighbours = 0;

        var items = new List<PagerItem>();

        items.Add(currentPage > 1
            ? new PagerItem(PagerItemKind.Previous, PageNumber: currentPage - 1)
            : new PagerItem(PagerItemKind.Previous, IsDisabled: true));

        var firstStart = 1;
        var firstEnd = Math.Min(edgeCount, totalPages);
        var lastStart = Math.Max(totalPages - edgeCount + 1, 1);
        var lastEnd = totalPages;
        var middleStart = Math.Max(currentPage - visibleNeighbours, 1);
        var middleEnd = Math.Min(currentPage + visibleNeighbours, totalPages);
        var hasMiddle = true;

        // Merge middle into first if they overlap on at least one page. Adjacent (touching
        // without sharing a page) intentionally does NOT merge — the ellipsis is a block
        // separator, not a "skipped pages" indicator.
        if (middleStart <= firstEnd)
        {
            firstEnd = Math.Max(firstEnd, middleEnd);
            hasMiddle = false;
        }

        // Same rule on the other side. Note the middle block may have been consumed by
        // the first-side merge above — in that case the local reads still work because
        // the middleStart/End values are unchanged, they simply refer to a range now
        // contained within the first block.
        if (hasMiddle && middleEnd >= lastStart)
        {
            lastStart = Math.Min(lastStart, middleStart);
            hasMiddle = false;
        }

        // If, after middle-merging, the first and last blocks themselves overlap or touch,
        // collapse everything into a single continuous range. Threshold `>= lastStart - 1`
        // catches both overlap (firstEnd >= lastStart) and adjacency (firstEnd + 1 == lastStart).
        var hasLast = true;
        if (firstEnd >= lastStart - 1)
        {
            firstEnd = Math.Max(firstEnd, lastEnd);
            hasLast = false;
        }

        for (var p = firstStart; p <= firstEnd; p++)
        {
            items.Add(new PagerItem(PagerItemKind.Page, PageNumber: p, IsCurrent: p == currentPage));
        }

        if (hasMiddle)
        {
            items.Add(new PagerItem(PagerItemKind.Ellipsis));
            for (var p = middleStart; p <= middleEnd; p++)
            {
                items.Add(new PagerItem(PagerItemKind.Page, PageNumber: p, IsCurrent: p == currentPage));
            }
        }

        if (hasLast)
        {
            items.Add(new PagerItem(PagerItemKind.Ellipsis));
            for (var p = lastStart; p <= lastEnd; p++)
            {
                items.Add(new PagerItem(PagerItemKind.Page, PageNumber: p, IsCurrent: p == currentPage));
            }
        }

        items.Add(currentPage < totalPages
            ? new PagerItem(PagerItemKind.Next, PageNumber: currentPage + 1)
            : new PagerItem(PagerItemKind.Next, IsDisabled: true));

        return items;
    }
}
