using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public class UrnAmendmentRequestDto
{
    public required string PupilName { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateTime Submitted { get; init; }
    public string WindowName { get; init; }
    public Guid WindowId { get; init; }
}