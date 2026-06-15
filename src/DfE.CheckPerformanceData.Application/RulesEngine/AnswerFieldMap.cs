namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Maps the producer side's vocabulary (journey question ids and option values on
/// the message, <c>WhatToChange</c> contract strings) onto the engine's canonical
/// names. Updating either side requires touching only this file.
/// <c>QuestionFlowOutcomeKeyAlignmentTests</c> pins the Web flow configs to these
/// maps in CI.
/// </summary>
/// <remarks>
/// This indirection is deliberate — it is an anti-corruption boundary so the rules
/// (Application) layer never depends on Web journey-authoring ids. The relationship
/// is not 1:1: <c>sat-exams</c> resolves by checking window type (<see cref="SatExamsFieldFor"/>),
/// single-choice radios fan out to several booleans (<see cref="RadioFanOut"/>),
/// some answers carry journey vocabulary the rules don't use
/// (<see cref="TranslatedQuestions"/>), and journey-only questions
/// (<c>reason</c>, <c>evidence</c>, …) are intentionally projected out.
/// Question ids are also the contract for serialized in-flight draft state, so a
/// rename would be a breaking change with no migration path.
///
/// Fields with no producer source yet — no journey question collects them — stay
/// <c>Unknown</c> so their rules defer to Scrutiny:
/// <c>illnessHasSevereProfoundEffect</c>, <c>whereaboutsKnown</c>,
/// <c>locatedAfterReasonableEfforts</c>.
/// </remarks>
public static class AnswerFieldMap
{
    /// <summary>
    /// Pupil inclusion status code meaning the pupil was added back to the
    /// school's results. <c>isAddBack</c> is calculated from the pupil record:
    /// <c>true</c> when <c>Pincl</c> equals this code, <c>false</c> for any other
    /// supplied code, <c>Unknown</c> when <c>Pincl</c> is not supplied.
    /// </summary>
    public const int AddBackPincl = 403;

    /// <summary>
    /// <c>Answer.QuestionId</c> on the queue message → canonical field name in
    /// <see cref="FieldCatalogue"/>, for answers whose value is copied as-is and
    /// parsed by the field's type. An entry's presence here (or in
    /// <see cref="RadioFanOut"/>/<see cref="TranslatedQuestions"/>/<see cref="SatExamsQuestionId"/>)
    /// is what makes the mapper consider an answer at all; unmapped answers are
    /// silently ignored.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> QuestionToField =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // EAL (Remove - english-not-first-language)
            ["country-originally-from"]                = "countryOfOrigin",
            ["date-pupil-started"]                     = "schoolAdmissionDate",
            ["date-pupil-started-school-in-england"]   = "firstSchoolAdmissionDate",
            ["date-pupil-arrived-in-england"]          = "dateOfArrivalInEngland",

            // Exclusion / removal dates. Two different journey questions feed
            // dateOfPermanentExclusion: date-pupil-excluded on the "admitted following
            // permanent exclusion" branch, date-permanently-excluded on the
            // "permanently excluded from current school" branch.
            ["date-pupil-excluded"]                    = "dateOfPermanentExclusion",
            ["date-permanently-excluded"]              = "dateOfPermanentExclusion",
            ["date-removed-from-roll"]                 = "dateOfRemoval",

            // Provisional ids for flows not yet authored (KS2 / Post16) — revisit
            // when those configs exist; the alignment test allowlists them.
            ["date-added-to-roll"]                     = "dateAddedToRoll",
            ["removal-reason-at-school"]               = "removalReasonAtSchool",
            ["continuing-ks2-studies"]                 = "isContinuingKS2Studies",
            ["pupil-age"]                              = "pupilAge",
        };

    /// <summary>
    /// Single-choice radio questions the rules model as independent booleans:
    /// the selected option's field becomes <c>true</c>, every other listed field
    /// <c>false</c>. An empty answer leaves all fields <c>Unknown</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Field, string TriggerValue)>> RadioFanOut =
        new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.Ordinal)
        {
            ["social-care-reason"] =
            [
                ("hadSocialCareInvolvement",     "social-care-situation"),
                ("hadRecentPoliceInvolvement",   "police-involvement"),
                ("hasBeenDetainedInPrison",      "detained-in-prison"),
            ],
            ["life-limiting-illness-health-issue"] =
            [
                ("hasTerminalIllness",             "life-limiting"),
                ("hasCriticalIllness12mPlus",      "twelve-months-critically-ill"),
                ("hasRecentLifeChangingDiagnosis", "life-changing-illness"),
                ("hasRecentLifeChangingInjury",    "life-changing-injury"),
                ("underInvestigation12mPlus",      "investigated"),
            ],
        };

    /// <summary>
    /// Questions whose journey option values translate into a different canonical
    /// vocabulary. Unlisted raw values resolve to <see cref="FieldValue.Unknown"/>
    /// (fail safe → Scrutiny); "believed" answers become
    /// <see cref="FieldValue.Uncertain"/> so <c>isKnownAndCertain</c> rules see them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Field, IReadOnlyDictionary<string, FieldValue> Values)> TranslatedQuestions =
        new Dictionary<string, (string, IReadOnlyDictionary<string, FieldValue>)>(StringComparer.Ordinal)
        {
            ["first-language"] = ("firstLanguage", new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["english"]          = new FieldValue.Str("ENG"),
                ["believed-english"] = new FieldValue.Uncertain(new FieldValue.Str("ENG")),
                ["other"]            = new FieldValue.Str("OTH"),
                ["believed-other"]   = new FieldValue.Uncertain(new FieldValue.Str("OTH")),
                // chose-not-to-say / not-known fall through to Unknown.
            }),
            ["higher-lower"] = ("yearGroupChange", new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["higher"] = new FieldValue.Str("Higher"),
                ["lower"]  = new FieldValue.Str("Lower"),
            }),
        };

    /// <summary>
    /// The journey asks one <c>sat-exams</c> yes/no question; the rules distinguish
    /// the year-11 and year-6 variants by checking window type.
    /// </summary>
    public const string SatExamsQuestionId = "sat-exams";

    /// <summary>Canonical field for <see cref="SatExamsQuestionId"/>, or null when the
    /// checking window type has no sat-exams concept (the answer is then ignored).</summary>
    public static string? SatExamsFieldFor(string checkingWindowType) => checkingWindowType switch
    {
        "KS4June" or "KS4Autumn" => "hasSatExamsAsYear11",
        "KS2" => "hasSatExamsAsYear6",
        _ => null,
    };

    /// <summary>
    /// <c>RequestDocument.RequestTypeCode</c> contract string → canonical outcome key used
    /// by <see cref="OutcomeRules.Key"/> in the rules JSON. The contract string is the
    /// <c>WhatToChange</c> enum name, suffixed with <c>" - {option value}"</c> when the
    /// flow config flags a question with <c>useAsRequestType</c> (the Remove flows'
    /// <c>reason</c> radio). Option *values* are used — not display labels — so UI copy
    /// changes cannot break routing; <c>QuestionFlowOutcomeKeyAlignmentTests</c> pins
    /// the flow configs to this map in CI. Lookup is case-insensitive and tolerant of
    /// trailing/leading whitespace.
    ///
    /// Outcomes with no journey flow yet (<c>CompletedKs4Elsewhere</c>,
    /// <c>AssessmentsDeferred</c>, <c>PupilAddedAfterSummerTerm</c>,
    /// <c>PupilNotOnJuneList</c>, <c>NotAtEndOf16To18Study</c>, <c>Other</c>) have no
    /// entry here; add one when the KS2/Post16 flows are authored
    /// (see <c>SeedRulesValidationTests.PendingJourneyOutcomeKeys</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WhatToChangeToOutcomeKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Merge"]                                  = "MergePupils",
            ["Include"]                                = "Inclusion",

            // Remove flow reason option values. Note the near-miss pair:
            // "permanent-exclusion" = admitted *following* a permanent exclusion
            // elsewhere; "permanently-excluded" = excluded *from the current school*.
            ["Remove - permanent-exclusion"]           = "AdmittedFollowingPermanentExclusion",
            ["Remove - english-not-first-language"]    = "AdmittedFromAbroadEal",
            ["Remove - child-missing-education"]       = "PupilMissingInEducation",
            ["Remove - pupil-died"]                    = "Deceased",
            ["Remove - dual-registered-moved"]         = "MovedSchoolDualRegistration",
            ["Remove - elective-home-education"]       = "ElectiveHomeEducation",
            ["Remove - not-on-roll"]                   = "NotOnRoll",
            ["Remove - permanently-excluded"]          = "PermanentlyExcludedFromCurrentSchool",
            ["Remove - permanently-left-england"]      = "PermanentlyLeftEngland",
            ["Remove - social-care-involvement"]       = "SocialCareInvolvement",
            ["Remove - life-limiting-illness"]         = "TerminalCriticalIllness",
            ["Remove - year-group-change"]             = "YearGroupChange",
        };

    /// <summary>
    /// Normalises <c>CheckingWindowType</c> values onto the canonical
    /// <c>KS2</c> / <c>KS4June</c> / <c>KS4Autumn</c> / <c>Post16</c>, case-insensitively.
    /// The legacy docx phrasings <c>"16 to 18"</c> / <c>"16-18"</c> map to <c>Post16</c>.
    /// Unrecognised values (including a bare <c>"KS4"</c>, ambiguous between June and
    /// Autumn) pass through trimmed: they match no rule predicate, so evaluation falls
    /// to the outcome's <c>otherwise</c> branch — Scrutiny, the fail-safe.
    /// </summary>
    public static string NormaliseCheckingWindowType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Equals("KS2", StringComparison.OrdinalIgnoreCase)) return "KS2";
        if (trimmed.Equals("KS4June", StringComparison.OrdinalIgnoreCase)) return "KS4June";
        if (trimmed.Equals("KS4Autumn", StringComparison.OrdinalIgnoreCase)) return "KS4Autumn";
        if (trimmed.Equals("Post16", StringComparison.OrdinalIgnoreCase)) return "Post16";
        if (trimmed.Equals("16 to 18", StringComparison.OrdinalIgnoreCase)) return "Post16";
        if (trimmed.Equals("16-18", StringComparison.OrdinalIgnoreCase)) return "Post16";
        return trimmed;
    }

    /// <summary>Constant used for outcome key when the reason cannot be mapped.</summary>
    public const string UnknownOutcomeKey = "_unknown";
}
