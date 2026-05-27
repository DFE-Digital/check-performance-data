namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Maps the producer side's vocabulary (form <c>QuestionId</c>s on the message,
/// docx <c>WhatToChange</c> reason strings) onto the engine's canonical names.
///
/// At time of writing the Web producer is a stub (<c>HomeController.SendRequest</c>
/// enqueues a placeholder string) — these are the IDs the producer will target
/// when it is built out. Updating either side requires touching only this file.
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
    /// Docx outcome label (as it appears on a <c>WhatToChange</c> form answer)
    /// → canonical outcome key used by <see cref="OutcomeRules.Key"/> in the rules JSON.
    /// Lookup is case-insensitive and tolerant of trailing/leading whitespace.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WhatToChangeToOutcomeKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inclusion"]                                                                       = "Inclusion",
            ["Admitted following permanent exclusion"]                                          = "AdmittedFollowingPermanentExclusion",
            ["Admitted from abroad with English not first language"]                            = "AdmittedFromAbroadEal",
            ["Completed KS4 studies this academic year in year 11 at another school or college"]= "CompletedKs4Elsewhere",
            ["Merge pupils"]                                                                    = "MergePupils",
            ["Social care involvement - including police/prison"]                               = "SocialCareInvolvement",
            ["Social care involvement including police/prison"]                                 = "SocialCareInvolvement",
            ["Terminal/Critical illness"]                                                       = "TerminalCriticalIllness",
            ["Year group change"]                                                               = "YearGroupChange",
            ["Deceased"]                                                                        = "Deceased",
            ["Elective home education"]                                                         = "ElectiveHomeEducation",
            ["Moved school/Dual registration"]                                                  = "MovedSchoolDualRegistration",
            ["Not on roll"]                                                                     = "NotOnRoll",
            ["Permanently excluded from current school"]                                        = "PermanentlyExcludedFromCurrentSchool",
            ["Permanently left England"]                                                        = "PermanentlyLeftEngland",
            ["Pupil missing in Education"]                                                      = "PupilMissingInEducation",
            ["One or more end-of-key stage assessments deferred by a year"]                     = "AssessmentsDeferred",
            ["Pupil added to school roll after start of summer term"]                           = "PupilAddedAfterSummerTerm",
            ["Pupil not on June list"]                                                          = "PupilNotOnJuneList",
            ["Not at end of 16 to 18 study"]                                                    = "NotAtEndOf16To18Study",
            ["Other"]                                                                           = "Other",
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
