using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The full transactions list: a paged, newest-first view of every recorded queue metric event.
// Paging is by the Wiki:PageLength setting and done in SQL (the rows are one page only); the
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
}
