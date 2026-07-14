namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class BulkConfirmationViewModel
{
    public required Guid WindowId { get; init; }
    public required IReadOnlyList<string> ReferenceNumbers { get; init; }
    public required string WindowCloseLabel { get; init; }
}
