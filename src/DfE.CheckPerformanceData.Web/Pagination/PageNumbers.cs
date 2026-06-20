namespace DfE.CheckPerformanceData.Web.Pagination;

// One entry in a rendered pager: either a page number (possibly the current one) or an ellipsis
// standing in for a run of hidden pages.
public readonly record struct PageItem(int Number, bool IsEllipsis, bool IsCurrent)
{
    public static PageItem For(int number, bool current) => new(number, false, current);
    public static readonly PageItem Ellipsis = new(0, true, false);
}

// Builds the windowed page-number list every pager renders: first and last page always shown, the
// current page with one neighbour on each side, an ellipsis for any gap of more than one hidden
// page, and a single hidden page shown rather than elided. Pure and view-agnostic so the shared
// _Pager partial only renders what this returns — the "1 … 4 5 6 … 82" best-practice shape rather
// than every page in a long list.
public static class PageNumbers
{
    public static IReadOnlyList<PageItem> Build(int currentPage, int totalPages)
    {
        if (totalPages < 1)
            return Array.Empty<PageItem>();

        if (currentPage < 1) currentPage = 1;
        if (currentPage > totalPages) currentPage = totalPages;

        // The pages always kept visible: first, last, and the current page with one neighbour
        // either side. A SortedSet de-duplicates and orders them.
        var wanted = new SortedSet<int> { 1, totalPages };
        for (var p = currentPage - 1; p <= currentPage + 1; p++)
            if (p >= 1 && p <= totalPages)
                wanted.Add(p);

        var items = new List<PageItem>();
        var previous = 0;
        foreach (var p in wanted)
        {
            if (previous != 0)
            {
                var gap = p - previous;
                if (gap == 2)
                    // Exactly one page hidden between two shown pages — show it rather than collapse
                    // a single page to an ellipsis (which reads as "more pages" when there is one).
                    items.Add(PageItem.For(previous + 1, previous + 1 == currentPage));
                else if (gap > 2)
                    items.Add(PageItem.Ellipsis);
            }

            items.Add(PageItem.For(p, p == currentPage));
            previous = p;
        }

        return items;
    }
}
