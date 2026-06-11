using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class JourneySubmissionContext
{
    public Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }

    /// <summary>
    /// The rules-engine contract string: the <c>WhatToChange</c> enum name, suffixed
    /// with <c>" - {reason option value}"</c> when the flow has a
    /// <c>useAsRequestType</c> question (e.g. <c>"Remove - pupil-died"</c>).
    /// Keys of <c>AnswerFieldMap.WhatToChangeToOutcomeKey</c> must match these.
    /// </summary>
    public required string WhatToChange { get; init; }
    public required PupilDto Pupil { get; init; }
    public PupilDto? MatchedPupil { get; init; }
    public required CheckingWindowDto CheckingWindow { get; init; }
    public required Dictionary<string, QuestionAnswer> Answers { get; init; }
    public required List<string> History { get; init; }
}
