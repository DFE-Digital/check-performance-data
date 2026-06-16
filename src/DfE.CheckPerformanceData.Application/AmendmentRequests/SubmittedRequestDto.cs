using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class SubmittedRequestDto
{
    public required string PupilName { get; init; }
    public required string RequestType { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateTime Submitted { get; init; }
}
