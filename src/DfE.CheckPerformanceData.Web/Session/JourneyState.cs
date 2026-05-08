using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Session;

public sealed class JourneyState
{
    public NextSteps? SelectedNextStep { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public PupilDto SelectedPupil { get; set; }
}
