using DfE.CheckPerformanceData.Domain.Enums;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public abstract class RequestMessage
{
    public string Status { get; set; }
    public string ReferenceNumber { get; set; }
    public DateTime SubmittedAt { get; set; }
    public SubmittedBy SubmittedBy { get; set; }
    public Guid CheckingWindowId { get; set; }
    public string CheckingWindowType { get; set; }
    public string WhatToChange { get; set; }
    public School School { get; set; }
    public Pupil Pupil { get; set; }
    public List<Answer> Answers { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DecisionType DecisionType { get; set; } = DecisionType.Scrutiny;

    public Guid RequestId { get; set; }
}

public sealed class School
{
    public string Urn { get; set; }
    public string Name { get; set; }
}

public sealed class SubmittedBy
{
    public string UserId { get; set; }
    public string DisplayName { get; set; }
}

public sealed class Answer
{
    public string QuestionId { get; set; }
    public string QuestionTitle { get; set; }
    public string Type { get; set; }
    public string Value { get; set; }
    public List<File> Files { get; set; }
}

public sealed class File
{
    public string OriginalFileName { get; set; }
    public string StoredFileName { get; set; }
    public int PageCount { get; set; }
    public int FileSizeBytes { get; set; }
}

public sealed class Pupil
{
    public string Id { get; set; }
    public string CypmdId { get; set; }
    public string Firstname { get; set; }
    public string Surname { get; set; }
    public string DateOfBirth { get; set; }
    public string Sex { get; set; }
    public int Age { get; set; }
}