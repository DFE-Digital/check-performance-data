using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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
}
