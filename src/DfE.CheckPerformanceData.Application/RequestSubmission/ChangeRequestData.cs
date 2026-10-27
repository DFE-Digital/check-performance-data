using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class ChangeRequestData
{
    public required Guid WindowId { get; init; }

    /// <summary>
    /// The <c>CheckingExercises</c> row this request belongs to. Resolved by the caller through
    /// <c>ICheckingExerciseService.IdFor</c>; null when the window has no row for the exercise the
    /// request's change type maps to.
    /// </summary>
    public Guid? CheckingExerciseId { get; init; }

    /// <summary>
    /// The exercise's current release when the request was saved: the data the school saw. Null
    /// when the exercise has no release.
    /// </summary>
    public Guid? CheckingExerciseReleaseId { get; init; }
    public required string ReferenceNumber { get; init; }
    public required long OrganisationUrn { get; init; }
    public Guid? PupilId { get; init; }
    public string? PupilUpn { get; init; }
    public string? PupilFirstname { get; init; }
    public string? PupilSurname { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Guid SubmittedById { get; init; }
    public required string SubmittedByName { get; init; }
    public string? SubmittedByEmail { get; init; }
    public required RequestStatus Status { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public WhatToChange? AmendmentType { get; init; }

    /// <summary>The school's LAESTAB from the DfE Sign-In claim; null when the claim is empty (AB#294553).</summary>
    public string? OrganisationLaestab { get; init; }

    /// <summary>
    /// Shows how far the request is on its way to Zendesk. Null for a draft and for an amendment;
    /// the Rules Engine sets it later. A results enquiry is written as
    /// <see cref="ProcessingStatus.TicketQueued"/>, because it is queued for its ticket at submit (#536).
    /// </summary>
    public ProcessingStatus? ProcessingStatus { get; init; }

    /// <summary>The decision, when it is known at write time (Scrutiny for a results enquiry).</summary>
    public DecisionStatus? Outcome { get; init; }
}
