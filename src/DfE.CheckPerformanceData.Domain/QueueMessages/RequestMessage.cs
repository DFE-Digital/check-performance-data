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

  
    [JsonConverter(typeof(JsonStringEnumConverter))] // todo - consider/ discuss using newtonsoft instead of sytem.text.json
    public DecisionType DecisionType { get; set; } = DecisionType.Scrutiny; // this is not in the json. disuss this with the team

    
    public Guid RequestId { get; set; }  // should this come from the queue message metadata instead of the message body?

    public abstract Task ProcessAsync(CancellationToken token); // this is not needed if we use the request decision handler to centralize the processing of all request messages, but we can keep it here for now and implement it in the future if needed when we have more clarity on the processing logic and how it will differ between different decision types
}

public class School
{
    public string Urn { get; set; }
    public string Name { get; set; }
}

public class SubmittedBy
{
    public string UserId { get; set; }
    public string DisplayName { get; set; }
}
public class Answer
{
    public string QuestionId { get; set; }
    public string QuestionTitle { get; set; }
    public string Type { get; set; }
    public string Value { get; set; }
    public List<File> Files { get; set; }
}

public class File
{
    public string OriginalFileName { get; set; }
    public string StoredFileName { get; set; }
    public int PageCount { get; set; }
    public int FileSizeBytes { get; set; }
}

public class Pupil
{
    public string Id { get; set; }
    public string CypmdId { get; set; }
    public string Firstname { get; set; }
    public string Surname { get; set; }
    public string DateOfBirth { get; set; }
    public string Sex { get; set; }
    public int Age { get; set; }
}