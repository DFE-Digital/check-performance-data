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
    /// <c>pupil-died</c>). The option VALUE strings are read from the Zendesk field's
    /// option list — not synthesised (FR-010). <c>4_31</c> is the documented example
    /// from FR-010 (deceased → code 4).
    /// </summary>
    public static class CorrectionReason31
    {
        public const string PupilDied = "4_31";
    }

    /// <summary>
    /// Options for "Decision Reason - Approved" tagger field (ID: 19056477269010), keyed on
    /// the rules-engine <c>Decision.OutcomeKey</c>. The option strings are the field's EXACT
    /// tagger values retrieved from the Zendesk API (FR-013) — they do not follow a single
    /// concatenation pattern, so they are curated here rather than generated.
    /// </summary>
    public static class DecisionReasonApproved
    {
        public const string Deceased = "deceased_criteria_met";
        public const string NotOnRoll = "not_on_roll_apprentice_criteria_met";
        public const string YearGroupChange = "year_group_changed_to_year_10";
        public const string TerminalCriticalIllness = "terminal/critical_illness_-_criteria_met";
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
            // (spec field table, field ID 19381440546322). The "pupil died" removal
            // reason maps to option "deceased". The remaining removal-reason options
            // are not yet confirmed; unmapped reasons are omitted + warning-logged (FR-014).
            { "terminal_critical_illness", ReasonForRemoval.TerminalCriticalIllness },
            { "terminal critical illness", ReasonForRemoval.TerminalCriticalIllness },
            { "pupil-died", ReasonForRemoval.Deceased },
            { "pupil died", ReasonForRemoval.Deceased }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class CorrectionReason31Helpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // Documented example from FR-010: deceased -> code 4. The remaining
            // removal-reason -> correction-code mappings are confirmed with the
            // Zendesk field option list during implementation (T003); unmapped
            // removal reasons are omitted + warning-logged (FR-014).
            { "pupil-died", CorrectionReason31.PupilDied }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    private static class DecisionReasonApprovedHelpers
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Deceased", DecisionReasonApproved.Deceased },
            { "NotOnRoll", DecisionReasonApproved.NotOnRoll },
            { "YearGroupChange", DecisionReasonApproved.YearGroupChange },
            { "TerminalCriticalIllness", DecisionReasonApproved.TerminalCriticalIllness }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    #endregion
}