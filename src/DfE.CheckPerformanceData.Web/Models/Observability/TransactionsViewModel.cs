using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The full transactions list: a paged, newest-first view of every recorded queue metric event.
// Paging is by the Wiki:PageLength setting and done in SQL (the rows are one page only); the
// total count drives the pager. An optional from/to window narrows the list (a nice-to-have on
// this page; the full filter lives on the submissions/replay page).
public sealed class TransactionsViewModel
{
    public IReadOnlyList<TransactionRow> Rows { get; init; } = [];

    // When "group by message" is on, the rows are one-per-message across the pipeline stages
    // (the same picture as the dashboard matrix) instead of one-per-event. Exactly one of Rows /
    // GroupedRows is populated, per the Grouped flag.
    public bool Grouped { get; init; }
    public IReadOnlyList<GroupedTransactionRow> GroupedRows { get; init; } = [];

    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    // The active reference search term (case-insensitive prefix), carried back so the search box
    // stays populated and the pager links preserve it across pages.
    public string? Reference { get; init; }

    // The ticked stage filters (Submitted / RulesEvaluated / TicketCreated / DeadLettered), carried
    // back so the checkboxes stay ticked and the pager/sort links preserve the filter.
    public IReadOnlyList<string> Stages { get; init; } = [];

    // The active sort: the validated column key and direction, so the headers render the active
    // caret and toggle direction, and the pager links preserve the sort.
    public string SortKey { get; init; } = TransactionSort.DefaultKey;
    public bool SortDescending { get; init; } = true;
}
