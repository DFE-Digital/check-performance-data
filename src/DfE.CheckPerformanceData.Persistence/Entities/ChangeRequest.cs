using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public class ChangeRequest
{
    public required Guid Id { get; init; }
    public required Guid WindowId { get; init; }
    public required long OrganisationUrn { get; init; }
    public required string PupilUpn { get; init; }
    public required string PupilFirstname { get; init; }
    public required string PupilSurname { get; init; }
    public required DateTime Submitted { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
    public required RequestStatus Status { get; set; }
    public required string ReferenceNumber { get; init; }
    public required string RequestType { get; init; }
    public string? CrmId { get; set; }
    public string? DecisionOutcomeKey { get; set; }
    public string? MatchedRuleId { get; set; }
    public string? RulesVersion { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}