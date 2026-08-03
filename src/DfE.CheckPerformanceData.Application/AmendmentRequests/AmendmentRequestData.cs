using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestData
{
    public Guid? PupilId { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required RequestStatus Status { get; init; }
    public required string ReferenceNumber { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? Submitted { get; init; }
    public string? WithdrawnByEmail { get; init; }
    public DateTime? WithdrawnAt { get; init; }
}
