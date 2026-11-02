using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class SubmittedRequestDto
{
    public required string PupilName { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateTime Submitted { get; init; }

    /// <summary>#746: the decision the pull stored on the row; null when it was never pulled.</summary>
    public ZendeskDecision? Decision { get; init; }

    /// <summary>#746: the reason label stored with the decision; null when Zendesk held none.</summary>
    public string? DecisionReason { get; init; }
}
