using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one below ambiguous.
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

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

    /// <summary>
    /// The word this journey's window uses for a learner — "student" on 16-19, "pupil" everywhere
    /// else. Derived from the window rather than stored, for the same reason the journey's checking
    /// exercise is: a stored copy can disagree with the journey it belongs to.
    ///
    /// Falls back to "pupil" when no window is in session. The journey cannot start without one
    /// (IsSessionReady), so this is a belt-and-braces default rather than a real path.
    /// </summary>
    public LearnerNoun LearnerNoun =>
        CheckingWindow is { } window ? LearnerNoun.For(window.CheckingWindowType) : LearnerNoun.Pupil;
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

    /// <summary>
    /// AB#027: the matching pupils found on the included list when the Include journey's
    /// select-pupil search matched an included pupil (the "already included" decision). Carried
    /// from the search POST to the "already included" page across the post/redirect/get round-trip,
    /// so the page can show who was matched. Transient — consumed by <c>AlreadyIncluded</c> on
    /// arrival; rows carry pupil PII and must never be logged or placed in analytics. Null when no
    /// "already included" decision is in flight.
    /// </summary>
    public List<PupilSuggestionDto>? IncludeMatchedPupils { get; set; }
}
