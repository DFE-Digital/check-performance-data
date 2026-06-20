using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>
/// Read-only projection of a submitted request, rebuilt from the persisted
/// <see cref="RequestState"/> journey plus its question flow config. Mirrors the
/// journey Summary content but carries no navigation/change targets.
/// </summary>
public sealed class SubmittedRequestView
{
    public required WhatToChange WhatToChange { get; init; }
    public required string PupilName { get; init; }
    public string? FirstRecordDisplay { get; init; }
    public string? SecondRecordDisplay { get; init; }
    public required IReadOnlyList<SubmittedRequestAnswerRow> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestFileRow> Files { get; init; }
    public required string ReferenceNumber { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
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
