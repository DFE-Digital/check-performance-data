using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.QuestionFlow;

namespace DfE.CheckPerformanceData.Web.Session;

public sealed class JourneyState
{
    public NextSteps? SelectedNextStep { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public PupilDto SelectedPupil { get; set; }
    public KeyStages? KeyStage { get; set; }
    public Dictionary<string, QuestionAnswer> QuestionAnswers { get; set; } = new();
    public List<string> QuestionHistory { get; set; } = new();
}
