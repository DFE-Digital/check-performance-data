using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>
/// Read-only projection of a "confirm pupil data is correct" submission. Unlike
/// <see cref="SubmittedRequestView"/> it carries no question answers or evidence — these
/// requests are a simple declaration, so only the submitter and reference are shown.
/// </summary>
public sealed class ConfirmDataCorrectView
{
    public required RequestStatus Status { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public required string ReferenceNumber { get; init; }
}
