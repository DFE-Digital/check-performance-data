using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>
/// Read-only projection of a submitted request, rebuilt from the persisted
/// <see cref="RequestState"/> journey plus its question flow config. Mirrors the
/// journey Summary content but carries no navigation/change targets.
/// </summary>
public sealed class SubmittedRequestView
{
    public required WhatToChange WhatToChange { get; init; }
    public required RequestStatus Status { get; init; }
    public required string PupilName { get; init; }

    /// <summary>
    /// The word the request's window uses for a learner, from the persisted journey state —
    /// "student" on 16-19. A submitted request is read back long after it was made, so the noun
    /// comes from the request's own window rather than from whatever the reader is looking at.
    /// </summary>
    public required LearnerNoun LearnerNoun { get; init; }
    public string? FirstRecordDisplay { get; init; }
    public string? SecondRecordDisplay { get; init; }
    public required IReadOnlyList<SubmittedRequestAnswerRow> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestFileRow> Files { get; init; }
    public required string ReferenceNumber { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public string? WithdrawnByEmail { get; init; }
    public DateTime? WithdrawnAt { get; init; }
}

public sealed class SubmittedRequestAnswerRow
{
    public required string Title { get; init; }
    public required string DisplayValue { get; init; }
}

public sealed class SubmittedRequestFileRow
{
    public required string OriginalFileName { get; init; }
    public required string StoredFileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public required int PageCount { get; init; }
}
