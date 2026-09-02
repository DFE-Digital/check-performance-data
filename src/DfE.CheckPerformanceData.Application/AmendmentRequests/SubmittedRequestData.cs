using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class SubmittedRequestData
{
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateTime Submitted { get; init; }
    public Guid WindowId { get; init; }
}
