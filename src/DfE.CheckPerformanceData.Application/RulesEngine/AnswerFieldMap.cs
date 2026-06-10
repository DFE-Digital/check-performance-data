namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Maps the producer side's vocabulary (form <c>QuestionId</c>s on the message,
/// <c>WhatToChange</c> contract strings) onto the engine's canonical names.
/// Updating either side requires touching only this file.
/// </summary>
/// <remarks>
/// This indirection is deliberate — it is an anti-corruption boundary so the rules
/// (Application) layer never depends on Web journey-authoring ids. It cannot be
/// removed by simply renaming the Journey question ids to canonical names, because
/// the relationship is not 1:1: the same concept resolves to a different canonical
/// field by key stage (e.g. <c>sat-exams</c> → <c>hasSatExamsAsYear11</c> for KS4
/// but <c>hasSatExamsAsYear6</c> for KS2), and journey-only questions
/// (<c>reason</c>, <c>evidence</c>, …) are intentionally projected out (see below).
/// Question ids are also the contract for serialized in-flight draft state, so a
/// rename would be a breaking change with no migration path.
/// </remarks>
public static class AnswerFieldMap
{
    /// <summary>
    /// <c>Answer.QuestionId</c> on the queue message → canonical field name in
    /// <see cref="FieldCatalogue"/>. An entry's presence here is what makes the
    /// mapper consider an answer at all; unmapped answers are silently ignored.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> QuestionToField =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inclusion-status-flag"]            = "inclusionFlag",

            ["first-language"]                   = "firstLanguage",
            ["country-of-origin"]                = "countryOfOrigin",
            ["is-add-back"]                      = "isAddBack",
            ["school-admission-date"]            = "schoolAdmissionDate",
            ["first-school-admission-date"]      = "firstSchoolAdmissionDate",
            ["date-of-arrival-in-england"]       = "dateOfArrivalInEngland",

            ["date-of-permanent-exclusion"]      = "dateOfPermanentExclusion",
            ["date-of-removal-from-roll"]        = "dateOfRemoval",
            ["date-added-to-roll"]               = "dateAddedToRoll",

            ["year-group-change"]                = "yearGroupChange",
            ["removal-reason-at-school"]         = "removalReasonAtSchool",

            ["social-care-involvement"]          = "hadSocialCareInvolvement",
            ["recent-police-involvement"]        = "hadRecentPoliceInvolvement",
            ["detained-in-prison"]               = "hasBeenDetainedInPrison",

            ["sat-exams-y11"]                    = "hasSatExamsAsYear11",
            ["sat-exams-y6"]                     = "hasSatExamsAsYear6",

            ["terminal-illness"]                 = "hasTerminalIllness",
            ["critical-illness-12m"]             = "hasCriticalIllness12mPlus",
            ["recent-life-changing-diagnosis"]   = "hasRecentLifeChangingDiagnosis",
            ["recent-life-changing-injury"]      = "hasRecentLifeChangingInjury",
            ["under-investigation-12m"]          = "underInvestigation12mPlus",
            ["severe-profound-effect"]           = "illnessHasSevereProfoundEffect",

            ["whereabouts-known"]                = "whereaboutsKnown",
            ["located-reasonable-efforts"]       = "locatedAfterReasonableEfforts",

            ["continuing-ks2-studies"]           = "isContinuingKS2Studies",

            ["pupil-age"]                        = "pupilAge",
        };

    /// <summary>
    /// <c>RequestDocument.WhatToChange</c> contract string → canonical outcome key used
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
    /// Normalises <c>CheckingWindowType</c> values onto <c>KS2</c> / <c>KS4</c> / <c>Post16</c>.
    /// The docx phrasing <c>"16 to 18"</c> maps to <c>Post16</c>.
    /// </summary>
    public static string NormaliseKeyStage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Equals("KS2", StringComparison.OrdinalIgnoreCase)) return "KS2";
        if (trimmed.Equals("KS4", StringComparison.OrdinalIgnoreCase)) return "KS4";
        if (trimmed.Equals("Post16", StringComparison.OrdinalIgnoreCase)) return "Post16";
        if (trimmed.Equals("16 to 18", StringComparison.OrdinalIgnoreCase)) return "Post16";
        if (trimmed.Equals("16-18", StringComparison.OrdinalIgnoreCase)) return "Post16";
        return trimmed;
    }

    /// <summary>Constant used for outcome key when the reason cannot be mapped.</summary>
    public const string UnknownOutcomeKey = "_unknown";
}
