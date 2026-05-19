using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class ChangeRequestData
{
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
    public required long OrganisationUrn { get; init; }
    public required string PupilUpn { get; init; }
    public required string PupilFirstname { get; init; }
    public required string PupilSurname { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
    public required RequestStatus Status { get; init; }
}
