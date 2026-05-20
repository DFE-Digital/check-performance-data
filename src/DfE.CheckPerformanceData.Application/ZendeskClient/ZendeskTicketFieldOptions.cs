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
        public const string TerminalCriticalIllness = "terminal_critical_illness";
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
            { "key stage 1", KeyStage.Ks1 },
            { "key stage 2", KeyStage.Ks2 },
            { "key stage 3", KeyStage.Ks3 }
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
            { "terminal_critical_illness", ReasonForRemoval.TerminalCriticalIllness },
            { "terminal critical illness", ReasonForRemoval.TerminalCriticalIllness }
        };

        public static string? GetOption(string name) => Map.TryGetValue(name, out var value) ? value : null;
    }

    #endregion
}