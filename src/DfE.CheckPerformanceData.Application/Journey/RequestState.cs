using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class RequestState
{
    public NextSteps? SelectedNextStep { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public PupilDto? SelectedPupil { get; set; }
    public string? MatchedPupilId { get; set; }
    public string? MatchedPupilLabel { get; set; }
    public PupilDto? MatchedPupil { get; set; }
    public CheckingWindowDto? CheckingWindow { get; set; }
    public string? ReferenceNumber { get; set; }
    public Dictionary<string, QuestionAnswer> QuestionAnswers { get; set; } = new();
    public List<string> QuestionHistory { get; set; } = new();

    // Stamped at submission so the read-only view of a submitted request can show a
    // "Submitted by" section without depending on the queued RequestDocument.
    public string? SubmittedByEmail { get; set; }
    public DateTime? SubmittedAt { get; set; }
}
