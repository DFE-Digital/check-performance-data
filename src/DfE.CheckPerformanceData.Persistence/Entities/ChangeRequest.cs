using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public class ChangeRequest
{
    public required Guid Id { get; init; }
    public required Guid WindowId { get; init; }
    public required long OrganisationUrn { get; init; }
    // Stable pupil identity from the source JSON file (PupilRecord.Id). This — not PupilUpn — is
    // the key used for duplicate-request detection and search exclusion, because a pupil may have
    // no UPN and every UPN-less pupil would otherwise share the same blank UPN and collide.
    // Nullable only so rows written before this column existed remain valid.
    public Guid? PupilId { get; init; }
    public string? PupilUpn { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required DateTime Submitted { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
    public string? SubmittedByEmail { get; init; }
    public string? WithdrawnByEmail { get; init; }
    public DateTime? WithdrawnAt { get; init; }
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
    public WorkerStatus? WorkerStatus { get; set; }
}