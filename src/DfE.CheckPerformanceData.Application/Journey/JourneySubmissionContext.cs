using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class JourneySubmissionContext
{
    public Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
    public WhatToChange WhatToChange { get; init; }
    public required PupilDto Pupil { get; init; }
    public PupilDto? MatchedPupil { get; init; }
    public required CheckingWindowDto CheckingWindow { get; init; }
    public required Dictionary<string, QuestionAnswer> Answers { get; init; }
    public required List<string> History { get; init; }
}
