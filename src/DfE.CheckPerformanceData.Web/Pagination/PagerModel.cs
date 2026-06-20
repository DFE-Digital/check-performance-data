namespace DfE.CheckPerformanceData.Web.Pagination;

// The model for the shared _Pager partial: the current page, the total page count, and a function
// that builds the URL for a given page number. Each list page supplies its own PageUrl closure so
// the one pager control preserves whatever query string (filters, sort, search, tab) that page
// carries, while the windowing and GDS markup live in exactly one place.
public sealed class PagerModel
{
    public required int CurrentPage { get; init; }
    public required int TotalPages { get; init; }
    public required Func<int, string> PageUrl { get; init; }
}
