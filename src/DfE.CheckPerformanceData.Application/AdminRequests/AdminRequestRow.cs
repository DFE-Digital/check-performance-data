using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

// One row on the admin requests page: a change request in the checking window the page
// is scoped to, with the rules-engine decision fields (all null until the rules engine
// has run). There is no window title here - the page names one window in its heading, so
// repeating it on every row said nothing.
public sealed record AdminRequestRow
{
    public required string ReferenceNumber { get; init; }
    public required long OrganisationUrn { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required string RequestTypeDescription { get; init; }

    // The checking exercise the request was raised under, resolved from the row's
    // CheckingExerciseId. Null on rows written before that column existed, which is why the
    // column reads "Not recorded" rather than guessing the window's only exercise.
    public CheckingExerciseType? Exercise { get; init; }
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
