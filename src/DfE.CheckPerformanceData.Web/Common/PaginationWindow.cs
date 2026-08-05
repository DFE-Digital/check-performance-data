namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// Chooses which page numbers a pagination control should show.
/// </summary>
/// <remarks>
/// Rendering every page is fine for three pages and unusable for three hundred: a school with a
/// few hundred pupils produced one link per page, so a keyboard user had to tab through the lot
/// to reach anything after the list, and a screen reader read every number out. The GOV.UK
/// pagination pattern shows the first and last page, the current page and its immediate
/// neighbours, and an ellipsis wherever a run has been skipped.
/// </remarks>
public static class PaginationWindow
{
    /// <summary>
    /// Returns the zero-based page indices to render, in order, with <c>null</c> marking an
    /// ellipsis where a run of pages has been omitted.
    /// </summary>
    /// <param name="currentPage">Zero-based index of the page being viewed.</param>
    /// <param name="totalPages">Total number of pages.</param>
    public static IReadOnlyList<int?> Build(int currentPage, int totalPages)
    {
        if (totalPages <= 0) return [];

        var last = totalPages - 1;
        var current = Math.Clamp(currentPage, 0, last);

        // First, last, and the current page with one either side. A set because these overlap
        // heavily at the ends of the range (current 0 contributes 0 and 1, which are also first
        // and possibly last).
        var shown = new SortedSet<int> { 0, last, current };
        if (current - 1 >= 0) shown.Add(current - 1);
        if (current + 1 <= last) shown.Add(current + 1);

        var entries = new List<int?>();
        int? previous = null;
        foreach (var page in shown)
        {
            if (previous is not null)
            {
                var gap = page - previous.Value;
                // Collapsing a single skipped page renders "1 … 3", which is no shorter than
                // "1 2 3" and costs the user a reachable link. Only collapse a run of two or more.
                if (gap == 2) entries.Add(previous.Value + 1);
                else if (gap > 2) entries.Add(null);
            }
            entries.Add(page);
            previous = page;
        }

        return entries;
    }
}
