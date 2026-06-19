using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public class ChangeRequest
{
    public required Guid Id { get; init; }
    public required Guid WindowId { get; init; }
    public required long OrganisationUrn { get; init; }
    public string? PupilUpn { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required DateTime Submitted { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
    public string? SubmittedByEmail { get; init; }
    public required RequestStatus Status { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public string? CrmId { get; init; }

    // Written by the rules engine consumer once it has decided on the request, and read
    // back by the Zendesk consumer; all stay null until the rules engine has run.
    public DecisionStatus? Outcome { get; set; }
    public string? OutcomeKey { get; set; }
    public string? MatchedRuleId { get; set; }
    public string? RulesVersion { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}