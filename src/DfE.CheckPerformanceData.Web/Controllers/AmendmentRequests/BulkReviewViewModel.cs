namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class BulkReviewViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required IReadOnlyList<BulkReviewItemViewModel> Submittable { get; init; }
    public required IReadOnlyList<BulkReviewItemViewModel> Duplicates { get; init; }
}

public sealed class BulkReviewItemViewModel
{
    public required string ReferenceNumber { get; init; }
    public required string PupilName { get; init; }
    public required string RequestTypeDescription { get; init; }
    public string? DuplicateReason { get; init; }
}
