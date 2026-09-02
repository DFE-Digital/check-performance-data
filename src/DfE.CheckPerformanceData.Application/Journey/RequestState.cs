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

    /// <summary>AB#297848: the qualification a missing-qualification enquiry is about, re-resolved
    /// server-side from the QualList reference so a forged posted QAN cannot enter the journey.
    /// Null until one is chosen.</summary>
    public QualificationReference? SelectedQualification { get; set; }

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

    /// <summary>AB#297780: the result of the Add-journey duplicate check, held between the
    /// learner-details interception and the duplicate-check warning page. Null when no check has
    /// run (or it found nothing). Rows carry pupil PII and must never be logged.</summary>
    public PupilDuplicateCheckResult? DuplicateCheck { get; set; }

    /// <summary>
    /// AB#027: the typed-but-not-selected pupil label carried from the Include journey's
    /// select-pupil search to the "Pupil not found" / "Already included" page across a
    /// post/redirect/get round-trip. Transient — only the typed entry that triggered the decision,
    /// consumed by the page action and kept out of URLs because it is PII (must never be logged).
    /// Null when no no-results decision is in flight.
    /// </summary>
    public string? IncludeSearchLabel { get; set; }
}
