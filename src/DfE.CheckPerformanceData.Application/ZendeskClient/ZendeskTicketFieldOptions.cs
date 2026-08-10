using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Constants for Zendesk ticket field option values (dropdown fields).
/// These are the "value" properties from the custom_field_options array
/// returned by the Zendesk API: /api/v2/ticket_fields/{id}.json
/// </summary>
public static class ZendeskTicketFieldOptions
{
    /// <summary>
    /// Options for "Type of Organisation" field (ID: 360013574700)
    /// </summary>
    public static class TypeOfOrganisation
    {
        public const string TrainingProvider = "training_provider";
        public const string Employer = "employer";
        public const string EmployerProvider = "employer_/_provider";
        public const string ApprenticeshipTrainingAgency = "apprenticeship_training_agency";
        public const string EducationSkillsFundingAgency = "education_skills_funding_agency";
    }

    /// <summary>
    /// Options for "Decision status" field (ID: 19056253670034)
    /// </summary>
    public static class DecisionStatus
    {
        public const string Scrutiny = "scrutiny";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string AutoApproved = "auto_approved";
        public const string AutoRejected = "auto_rejected";
    }

    /// <summary>
    /// Options for "Correction type" field (ID: 19056595594898)
    /// </summary>
    public static class CorrectionType
    {
        public const string Correction31 = "31_";
    }

    /// <summary>
    /// Options for "Key stage" field (ID: 19058126549778)
    /// </summary>
    public static class KeyStage
    {
        public const string Ks1 = "ks1";
        public const string Ks2 = "ks2";
        public const string Ks3 = "ks3";
        public const string Ks4 = "ks4";
    }

    /// <summary>
    /// Options for "Sex" field (ID: 19058550118802)
    /// </summary>
    public static class Sex
    {
        public const string Male = "m";
        public const string Female = "f";
    }

    /// <summary>
    /// Options for "Reason for removal" field (ID: 19381440546322)
    /// </summary>
    public static class ReasonForRemoval
    {
        public const string Deceased = "deceased";
        public const string TerminalCriticalIllness = "terminal_critical_illness";
    }

    /// <summary>
    /// Options for "Correction reason (31)" field (ID: 19058912556690), keyed on the
    /// removal-reason option value (the suffix of <c>RequestTypeCode</c>, e.g.
    /// <c>pupil-died</c>). The option VALUE strings are the CYPMD correction codes
    /// confirmed by the business (2026-08-06) formatted as <c>{code}_31</c>, matching
    /// the Zendesk field's option list exactly — not synthesised (FR-010). Only the
    /// top-level removal reasons reachable from the KS4 Remove flow are keyed in the
    /// helper map; the add-back reasons (<c>completed-ks4-elsewhere</c>, <c>other</c>)
    /// and the sub-reasons below are intentionally left unmapped this phase.
    /// </summary>
    public static class CorrectionReason31
    {
        public const string AdmittedFollowingPermanentExclusion = "13_31";
        public const string AdmittedFromAbroadEal = "2_31";
        public const string PupilMissingInEducation = "501_31";
        public const string PupilDied = "4_31";
        public const string MovedSchoolDualRegistration = "332_31";
        public const string ElectiveHomeEducation = "27_31";
        public const string NotOnRoll = "6_31";
        public const string PermanentlyExcludedFromCurrentSchool = "500_31";
        public const string PermanentlyLeftEngland = "3_31";
        public const string SocialCareInvolvement = "502_31";
        public const string TerminalCriticalIllness = "503_31";
        public const string YearGroupChange = "17_31";
    }

    /// <summary>
    /// Options for "Decision Reason - Approved" tagger field (ID: 19056477269010), keyed on
    /// the rules-engine <c>Decision.OutcomeKey</c>. The option strings are the field's EXACT
    /// tagger VALUES retrieved from the Zendesk API (FR-013) — they do not follow a single
    /// concatenation pattern and some differ from their display name (e.g. the value of
    /// <c>Pupil missing in education</c> is <c>pupil_missing_in_education_-_criteria_met</c>),
    /// so they are curated here verbatim rather than generated. Population is gated to
    /// auto-approved decisions in <c>ZendeskConsumer.AddDecisionReasonField</c>; outcomes
    /// whose decision is never auto-approved (e.g. <c>Other</c>) have no entry and are
    /// omitted + warning-logged (FR-014).
    /// </summary>
    public static class DecisionReasonApproved
    {
        public const string Inclusion = "inclusion_criteria_met";
        public const string AdmittedFollowingPermanentExclusion = "admitted_following_permanent_exclusion_criteria_met";
        public const string AdmittedFromAbroadEal = "admitted_from_abroad_with_english_not_first_language_criteria_met";
        public const string CompletedKs4Elsewhere = "add_back_removal_criteria_met";
        public const string MergePupils = "merge_pupils_criteria_met";
        public const string SocialCareInvolvement = "social_care_involvement_including_police/prison_-_criteria_met";
        public const string TerminalCriticalIllness = "terminal/critical_illness_-_criteria_met";
        public const string YearGroupChange = "year_group_changed_to_year_10";
        public const string Deceased = "deceased_criteria_met";
        public const string ElectiveHomeEducation = "elective_home_education_criteria_met";
        public const string MovedSchoolDualRegistration = "moved_school_dual_registration_criteria_met";
        public const string NotOnRoll = "not_on_roll_criteria_met";
        public const string PermanentlyExcludedFromCurrentSchool = "permanently_excluded_from_current_school_criteria_met";
        public const string PermanentlyLeftEngland = "permanently_left_england_criteria_met";
        public const string PupilMissingInEducation = "pupil_missing_in_education_-_criteria_met";
        public const string AssessmentsDeferred = "one_or_more_end_of_key_stage_assessments_deferred_by_a_year_criteria_met";
        public const string PupilAddedAfterSummerTerm = "pupil_added_to_school_roll_after_start_of_summer_term_criteria_met";
        public const string PupilNotOnJuneList = "pupil_not_on_june_list_criteria_met";
        public const string NotAtEndOf16To18Study = "not_at_end_of_16_to_18_study_criteria_met";
    }

    /// <summary>
    /// Options for "Decision Reason - Rejected" tagger field (ID: 19197936168210), keyed on
    /// the rules-engine <c>Decision.OutcomeKey</c>. The option strings are the field's EXACT
    /// tagger VALUES retrieved from the Zendesk API — the <c>*_criteria_not_met</c> counterparts
    /// of the approved map, but irregular and curated verbatim rather than derived (e.g. the
    /// rejected value for <c>SocialCareInvolvement</c> is
    /// <c>social_care_involvement_including_police_prison_criteria_not_met</c> — no slashes or
    /// hyphens, unlike the approved value). Population is gated to auto-rejected decisions in
    /// <c>ZendeskConsumer.AddDecisionReasonField</c> via
    /// <see cref="ZendeskTicketFieldSettings.PopulateDecisionReasonForAutoRejected"/>; outcomes
    /// whose decision is never auto-rejected (e.g. <c>Other</c>) have no entry and are omitted +
    /// warning-logged (FR-014). Post16 not-on-roll variants are unreachable this phase.
    /// </summary>
    public static class DecisionReasonRejected
    {
        public const string Inclusion = "inclusion_criteria_not_met";
        public const string AdmittedFollowingPermanentExclusion = "admitted_following_permanent_exclusion_criteria_not_met";
        public const string AdmittedFromAbroadEal = "admitted_from_abroad_with_english_not_first_language_criteria_not_met";
        public const string CompletedKs4Elsewhere = "add_back_removal_criteria_not_met";
        public const string MergePupils = "merge_pupils_criteria_not_met";
        public const string SocialCareInvolvement = "social_care_involvement_including_police_prison_criteria_not_met";
        public const string TerminalCriticalIllness = "terminal_critical_illness_criteria_not_met";
        public const string YearGroupChange = "year_group_change_criteria_not_met";
        public const string Deceased = "deceased_criteria_not_met";
        public const string ElectiveHomeEducation = "elective_home_education_criteria_not_met";
        public const string MovedSchoolDualRegistration = "moved_school_dual_registration_criteria_not_met";
        public const string NotOnRoll = "not_on_roll_criteria_not_met";
        public const string PermanentlyExcludedFromCurrentSchool = "permanently_excluded_from_current_school_criteria_not_met";
        public const string PermanentlyLeftEngland = "permanently_left_england_criteria_not_met";
        public const string PupilMissingInEducation = "pupil_missing_in_education_criteria_not_met";
        public const string AssessmentsDeferred = "one_or_more_end_of_key_stage_assessments_deferred_by_a_year_criteria_not_met";
        public const string PupilAddedAfterSummerTerm = "pupil_added_to_school_roll_after_start_of_summer_term_criteria_not_met";
        public const string PupilNotOnJuneList = "pupil_not_on_june_list_criteria_not_met";
        public const string NotAtEndOf16To18Study = "not_at_end_of_16_to_18_study_criteria_not_met";
    }

    /// <summary>
    /// Gets the option value for a given field name and option name.
    /// Returns null if the field or option is not recognized.
    /// </summary>
    public static string? GetOptionValue(string fieldName, string optionName)
    {
        return fieldName switch
        {
            ZendeskTicketFieldConstants.TypeOfOrganisationName => TypeOfOrganisationHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.DecisionStatusName => DecisionStatusHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.CorrectionTypeName => CorrectionTypeHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.KeyStageName => KeyStageHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.SexName => SexHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.ReasonForRemovalName => ReasonForRemovalHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.CorrectionReason31Name => CorrectionReason31Helpers.GetOption(optionName),
            ZendeskTicketFieldConstants.DecisionReasonApprovedName => DecisionReasonApprovedHelpers.GetOption(optionName),
            ZendeskTicketFieldConstants.DecisionReasonRejectedName => DecisionReasonRejectedHelpers.GetOption(optionName),
            _ => null
        };
    }

    #region Helper methods for each field

    private static class TypeOfOrganisationHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "training provider", TypeOfOrganisation.TrainingProvider },
            { "employer", TypeOfOrganisation.Employer },
            { "employer/provider", TypeOfOrganisation.EmployerProvider },
            { "employer / provider", TypeOfOrganisation.EmployerProvider },
            { "apprenticeship training agency", TypeOfOrganisation.ApprenticeshipTrainingAgency },
            { "education skills funding agency", TypeOfOrganisation.EducationSkillsFundingAgency }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class DecisionStatusHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "scrutiny", DecisionStatus.Scrutiny },
            { "approved", DecisionStatus.Approved },
            { "rejected", DecisionStatus.Rejected },
            { "auto_approved", DecisionStatus.AutoApproved },
            { "autoApproved", DecisionStatus.AutoApproved },
            { "auto_rejected", DecisionStatus.AutoRejected },
            { "autoRejected", DecisionStatus.AutoRejected }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class CorrectionTypeHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "31_", CorrectionType.Correction31 },
            { "correction 31", CorrectionType.Correction31 }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class KeyStageHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ks1", KeyStage.Ks1 },
            { "ks2", KeyStage.Ks2 },
            { "ks3", KeyStage.Ks3 },
            { "ks4", KeyStage.Ks4 },
            { "KS4June", KeyStage.Ks4 },
            { "KS4Autumn", KeyStage.Ks4 },
            { "key stage 1", KeyStage.Ks1 },
            { "key stage 2", KeyStage.Ks2 },
            { "key stage 3", KeyStage.Ks3 },
            { "key stage 4", KeyStage.Ks4 }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class SexHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "m", Sex.Male },
            { "male", Sex.Male },
            { "f", Sex.Female },
            { "female", Sex.Female }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class ReasonForRemovalHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // Authoritative Zendesk option strings from the field's option list
            // (spec field table, field ID 19381440546322). Keyed on the RequestTypeCode
            // removal-reason suffix (the flow option value), e.g. "life-limiting-illness".
            // The "pupil died" removal reason maps to option "deceased". The remaining
            // removal-reason options are not yet confirmed; unmapped reasons are omitted +
            // warning-logged (FR-014).
            { "pupil-died", ReasonForRemoval.Deceased },
            { "pupil died", ReasonForRemoval.Deceased },
            { "life-limiting-illness", ReasonForRemoval.TerminalCriticalIllness }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class CorrectionReason31Helpers
    {
        // Keyed on the RequestTypeCode removal-reason suffix (e.g. "pupil-died").
        // Authoritative CYPMD correction codes (correction type 31) confirmed by the
        // business 2026-08-06; the value is the code suffixed with "_31". The add-back
        // removal reasons ("completed-ks4-elsewhere", "other") carry a different
        // correction type and are deliberately absent -> omitted + warning-logged (FR-014).
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "permanent-exclusion", CorrectionReason31.AdmittedFollowingPermanentExclusion },
            { "english-not-first-language", CorrectionReason31.AdmittedFromAbroadEal },
            { "child-missing-education", CorrectionReason31.PupilMissingInEducation },
            { "pupil-died", CorrectionReason31.PupilDied },
            { "dual-registered-moved", CorrectionReason31.MovedSchoolDualRegistration },
            { "elective-home-education", CorrectionReason31.ElectiveHomeEducation },
            { "not-on-roll", CorrectionReason31.NotOnRoll },
            { "permanently-excluded", CorrectionReason31.PermanentlyExcludedFromCurrentSchool },
            { "permanently-left-england", CorrectionReason31.PermanentlyLeftEngland },
            { "social-care-involvement", CorrectionReason31.SocialCareInvolvement },
            { "life-limiting-illness", CorrectionReason31.TerminalCriticalIllness },
            { "year-group-change", CorrectionReason31.YearGroupChange }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class DecisionReasonApprovedHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Inclusion", DecisionReasonApproved.Inclusion },
            { "AdmittedFollowingPermanentExclusion", DecisionReasonApproved.AdmittedFollowingPermanentExclusion },
            { "AdmittedFromAbroadEal", DecisionReasonApproved.AdmittedFromAbroadEal },
            { "CompletedKs4Elsewhere", DecisionReasonApproved.CompletedKs4Elsewhere },
            { "MergePupils", DecisionReasonApproved.MergePupils },
            { "SocialCareInvolvement", DecisionReasonApproved.SocialCareInvolvement },
            { "TerminalCriticalIllness", DecisionReasonApproved.TerminalCriticalIllness },
            { "YearGroupChange", DecisionReasonApproved.YearGroupChange },
            { "Deceased", DecisionReasonApproved.Deceased },
            { "ElectiveHomeEducation", DecisionReasonApproved.ElectiveHomeEducation },
            { "MovedSchoolDualRegistration", DecisionReasonApproved.MovedSchoolDualRegistration },
            { "NotOnRoll", DecisionReasonApproved.NotOnRoll },
            { "PermanentlyExcludedFromCurrentSchool", DecisionReasonApproved.PermanentlyExcludedFromCurrentSchool },
            { "PermanentlyLeftEngland", DecisionReasonApproved.PermanentlyLeftEngland },
            { "PupilMissingInEducation", DecisionReasonApproved.PupilMissingInEducation },
            { "AssessmentsDeferred", DecisionReasonApproved.AssessmentsDeferred },
            { "PupilAddedAfterSummerTerm", DecisionReasonApproved.PupilAddedAfterSummerTerm },
            { "PupilNotOnJuneList", DecisionReasonApproved.PupilNotOnJuneList },
            { "NotAtEndOf16To18Study", DecisionReasonApproved.NotAtEndOf16To18Study }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class DecisionReasonRejectedHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Inclusion", DecisionReasonRejected.Inclusion },
            { "AdmittedFollowingPermanentExclusion", DecisionReasonRejected.AdmittedFollowingPermanentExclusion },
            { "AdmittedFromAbroadEal", DecisionReasonRejected.AdmittedFromAbroadEal },
            { "CompletedKs4Elsewhere", DecisionReasonRejected.CompletedKs4Elsewhere },
            { "MergePupils", DecisionReasonRejected.MergePupils },
            { "SocialCareInvolvement", DecisionReasonRejected.SocialCareInvolvement },
            { "TerminalCriticalIllness", DecisionReasonRejected.TerminalCriticalIllness },
            { "YearGroupChange", DecisionReasonRejected.YearGroupChange },
            { "Deceased", DecisionReasonRejected.Deceased },
            { "ElectiveHomeEducation", DecisionReasonRejected.ElectiveHomeEducation },
            { "MovedSchoolDualRegistration", DecisionReasonRejected.MovedSchoolDualRegistration },
            { "NotOnRoll", DecisionReasonRejected.NotOnRoll },
            { "PermanentlyExcludedFromCurrentSchool", DecisionReasonRejected.PermanentlyExcludedFromCurrentSchool },
            { "PermanentlyLeftEngland", DecisionReasonRejected.PermanentlyLeftEngland },
            { "PupilMissingInEducation", DecisionReasonRejected.PupilMissingInEducation },
            { "AssessmentsDeferred", DecisionReasonRejected.AssessmentsDeferred },
            { "PupilAddedAfterSummerTerm", DecisionReasonRejected.PupilAddedAfterSummerTerm },
            { "PupilNotOnJuneList", DecisionReasonRejected.PupilNotOnJuneList },
            { "NotAtEndOf16To18Study", DecisionReasonRejected.NotAtEndOf16To18Study }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    #endregion
}