using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

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
    /// <summary>AB#296648: the exam result chosen on a ResultSearch page, re-resolved server-side
    /// from the results blob so a forged posted key cannot put an unheld result into the journey.
    /// Null until one is chosen.</summary>
    public StudentResultRecord? SelectedResult { get; set; }

    /// <summary>AB#296648: the display label of the exam result chosen on a ResultSearch page,
    /// re-rendered into the autocomplete on validation redisplay. Null until one is chosen.
    /// The typed record itself is <c>SelectedResult</c>.</summary>
    public string? SelectedResultLabel { get; set; }

    public CheckingWindowDto? CheckingWindow { get; set; }
    public string? ReferenceNumber { get; set; }
    public Dictionary<string, QuestionAnswer> QuestionAnswers { get; set; } = new();
    public List<string> QuestionHistory { get; set; } = new();

    /// <summary>ISO 3166-1 alpha-2 code of the answer to country-originally-from,
    /// resolved at details-page POST time (PBI 292266). Null when unresolved.</summary>
    public string? OriginCountryCode { get; set; }

    /// <summary>Official languages of <see cref="OriginCountryCode"/> per the
    /// country-languages.json lookup the rules engine also uses. Null when the
    /// country is unresolved or absent from the lookup.</summary>
    public List<string>? OriginCountryLanguages { get; set; }
}
