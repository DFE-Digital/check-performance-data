namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// The canonical list of fields the rules JSON is allowed to reference,
/// with their expected types. This is the single source of truth used by
/// <see cref="RuleSetValidator"/> (predicate validation), <see cref="RuleContextMapper"/>
/// (answer parsing), and as documentation for the rule authors.
///
/// Adding a new field here is a deliberate, reviewed change — it should be
/// accompanied by an update to <see cref="AnswerFieldMap"/> so the mapper
/// knows how to populate it from a message answer.
/// </summary>
public static class FieldCatalogue
{
    /// <summary>Canonical field name → expected runtime type.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> All =
        new Dictionary<string, FieldType>(StringComparer.Ordinal)
        {
            // Derived from message envelope
            ["checkingWindowType"]                = FieldType.String,
            ["requestType"]                       = FieldType.String,

            // Inclusion outcome
            ["inclusionFlag"]                     = FieldType.String,

            // EAL outcome
            ["firstLanguage"]                     = FieldType.String,
            ["countryOfOrigin"]                   = FieldType.String,
            ["isAddBack"]                         = FieldType.Bool,
            ["schoolAdmissionDate"]               = FieldType.Date,
            ["firstSchoolAdmissionDate"]          = FieldType.Date,
            ["dateOfArrivalInEngland"]            = FieldType.Date,

            // Exclusion / removal dates
            ["dateOfPermanentExclusion"]          = FieldType.Date,
            ["dateOfRemoval"]                     = FieldType.Date,
            ["dateAddedToRoll"]                   = FieldType.Date,

            // Discrete reason answers
            ["yearGroupChange"]                   = FieldType.String, // "Lower" | "Higher"
            ["removalReasonAtSchool"]             = FieldType.String, // not-on-roll-reason option values: apprentice | external-candidate | international-student | other

            // Social care / prison / police booleans
            ["hadSocialCareInvolvement"]          = FieldType.Bool,
            ["hadRecentPoliceInvolvement"]        = FieldType.Bool,
            ["hasBeenDetainedInPrison"]           = FieldType.Bool,

            // Exam-already-sat booleans
            ["hasSatExamsAsYear11"]               = FieldType.Bool,
            ["hasSatExamsAsYear6"]                = FieldType.Bool,

            // Illness/injury booleans
            ["hasTerminalIllness"]                = FieldType.Bool,
            ["hasCriticalIllness12mPlus"]         = FieldType.Bool,
            ["hasRecentLifeChangingDiagnosis"]    = FieldType.Bool,
            ["hasRecentLifeChangingInjury"]       = FieldType.Bool,
            ["underInvestigation12mPlus"]         = FieldType.Bool,
            ["illnessHasSevereProfoundEffect"]    = FieldType.Bool,

            // Missing-in-education booleans
            ["whereaboutsKnown"]                  = FieldType.Bool,
            ["locatedAfterReasonableEfforts"]     = FieldType.Bool,

            // KS2 deferred-assessments outcome
            ["isContinuingKS2Studies"]            = FieldType.Bool,

            // Age outcome
            ["pupilAge"]                          = FieldType.Number,
        };

    public static bool Contains(string field) => All.ContainsKey(field);

    public static bool TryGetType(string field, out FieldType type) => All.TryGetValue(field, out type);
}
