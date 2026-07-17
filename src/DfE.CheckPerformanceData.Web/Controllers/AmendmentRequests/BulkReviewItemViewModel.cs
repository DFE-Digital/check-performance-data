namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class BulkReviewItemViewModel
{
    public required string ReferenceNumber { get; init; }
    public required string PupilName { get; init; }
    public required string RequestTypeDescription { get; init; }
    public string? DuplicateReason { get; init; }
}
