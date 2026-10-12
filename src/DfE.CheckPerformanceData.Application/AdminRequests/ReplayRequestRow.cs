namespace DfE.CheckPerformanceData.Application.AdminRequests;

// A decided amendment the close sweep will send: enough to read its saved RequestDocument and to
// mark the row queued (#536).
public sealed record ReplayRequestRow
{
    public required Guid ChangeRequestId { get; init; }
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
}
