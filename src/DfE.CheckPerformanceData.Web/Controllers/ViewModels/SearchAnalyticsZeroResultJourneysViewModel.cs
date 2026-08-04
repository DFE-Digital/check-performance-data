using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View model for the /admin/Search/ZeroResultJourneys drill-in. Renders one card per
// session with a chronological chain of the searches that session ran in the window;
// only sessions with at least one zero-result event are included. The chain lets an
// editor see whether users refine their way to a hit ("scool → school → schools 15")
// or abandon after multiple failed tries. Ordered by chain-length DESC then session
// id ASC so the longest struggles surface first.
public sealed class SearchAnalyticsZeroResultJourneysViewModel
{
    public required IReadOnlyList<ZeroResultJourney> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }
    public required ZeroResultRecoveryStats RecoveryStats { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
