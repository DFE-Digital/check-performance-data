using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The full transactions list: a paged, newest-first view of every recorded queue metric event.
// Paging is by the CMS:PageLength setting and done in SQL (the rows are one page only); the
// total count drives the pager. An optional from/to window narrows the list (a nice-to-have on
// this page; the full filter lives on the submissions/replay page).
public sealed class TransactionsViewModel
{
    public IReadOnlyList<TransactionRow> Rows { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    // The active reference search term (case-insensitive prefix), carried back so the search box
    // stays populated and the pager links preserve it across pages.
    public string? Reference { get; init; }
}
