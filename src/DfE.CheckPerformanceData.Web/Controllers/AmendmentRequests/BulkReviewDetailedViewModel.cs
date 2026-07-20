using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

/// <summary>
/// Bulk review page: each submittable request is shown as a full journey-style summary (no
/// change links) rather than a one-line row, with duplicates in a compact warning table.
/// </summary>
public sealed class BulkReviewDetailedViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required IReadOnlyList<BulkReviewItemViewModel> Duplicates { get; init; }
    public required IReadOnlyList<BulkDetailedItemViewModel> Submittable { get; init; }
}

/// <summary>A submittable request paired with its full journey summary for the detailed view.</summary>
public sealed class BulkDetailedItemViewModel
{
    public required string ReferenceNumber { get; init; }
    public required SummaryViewModel Summary { get; init; }
}
