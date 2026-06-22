using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>
/// The fields needed to render the read-only view of a "confirm pupil data is correct"
/// request, read straight from the <c>ChangeRequests</c> row (these requests have no
/// persisted journey blob).
/// </summary>
public sealed class ConfirmDataCorrectData
{
    public required RequestType RequestType { get; init; }
    public string? SubmittedByEmail { get; init; }
    public required DateTime Submitted { get; init; }
    public required string ReferenceNumber { get; init; }
}
