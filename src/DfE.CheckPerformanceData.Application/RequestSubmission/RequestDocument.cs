namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class RequestDocument
{
    public required string ReferenceNumber { get; init; }
    public DateTime SubmittedAt { get; init; }
    public required UserDetails SubmittedBy { get; init; }
    public Guid CheckingWindowId { get; init; }
    public required string CheckingWindowType { get; init; }
    public required string WhatToChange { get; init; }
    public required SchoolDetails School { get; init; }
    public required PupilDetails Pupil { get; init; }
    public required List<AnswerRecord> Answers { get; init; }
}

public sealed class UserDetails
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}

public sealed class SchoolDetails
{
    public required string Urn { get; init; }
    public required string Name { get; init; }
}

public sealed class PupilDetails
{
    public required string Id { get; init; }
    public required string CypmdId { get; init; }
    public required string Firstname { get; init; }
    public required string Surname { get; init; }
    public required string DateOfBirth { get; init; }
    public required string Sex { get; init; }
    public required int Age { get; init; }
    public required string Upn { get; init; }
}

public sealed class AnswerRecord
{
    public required string QuestionId { get; init; }
    public required string QuestionTitle { get; init; }
    public required string Type { get; init; }
    public string? Value { get; init; }
    public List<FileRecord>? Files { get; init; }
}

public sealed class FileRecord
{
    public required string OriginalFileName { get; init; }
    public required string StoredFileName { get; init; }
    public required int PageCount { get; init; }
    public required long FileSizeBytes { get; init; }
}
