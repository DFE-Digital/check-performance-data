using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Landing view model for /admin/Messages/Inbox. Carries one page of message summaries
// (server-side truncated preview, has-email flag, is-read flag), the total count for
// pagination, the current sort selection so the header links can toggle direction, and
// the current filter text so the search input redisplays what the admin typed.
public sealed class MessagesInboxViewModel
{
    public required IReadOnlyList<SearchMessageSummary> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required string Sort { get; init; }
    public required string Dir { get; init; }
    public required string? Filter { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
