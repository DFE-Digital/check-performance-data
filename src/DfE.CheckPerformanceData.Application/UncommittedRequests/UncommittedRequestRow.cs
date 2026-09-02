using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

// One row on the admin "All requests" page: a change request in any checking
// window, with the rules-engine decision fields (all null until the rules engine
// has run).
public sealed record UncommittedRequestRow
{
    public required string ReferenceNumber { get; init; }
    public string? WindowTitle { get; init; }
    public required long OrganisationUrn { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required RequestStatus Status { get; init; }
    public required string SubmittedByName { get; init; }
    public required DateTime Submitted { get; init; }
    public DecisionStatus? Outcome { get; init; }
    public string? MatchedRule { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public string? CrmId { get; init; }

    // The winning branch's evaluation trace, newline-joined. Admin-only - this page is the
    // only place it is shown. Null until the rules engine has run, and on rows decided
    // before the column existed.
    public string? DecisionTrace { get; init; }
}
