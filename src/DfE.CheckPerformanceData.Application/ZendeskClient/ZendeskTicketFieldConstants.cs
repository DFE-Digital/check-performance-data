using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Constants for Zendesk ticket field names (not IDs).
/// Field IDs are stored in configuration (appsettings.json / user secrets) via ZendeskTicketFieldSettings.
/// Field names retrieved from Zendesk API: /api/v2/ticket_fields/{id}.json
/// </summary>
public static class ZendeskTicketFieldConstants
{
    // Field Name constants (not sensitive - safe to store in code)
    public const string TypeOfOrganisationName = "Type of Organisation";
    public const string DfeEstablishmentNumberName = "DfE Establishment Number";
    public const string UpnName = "UPN";
    public const string SchoolUrnName = "School URN";
    public const string LdsMatchedPupilIdName = "LDS matched pupil ID";
    public const string DateOfBirthCypmdName = "Date of birth (CYPMD)";
    public const string AdmissionDateName = "Admission date";
    public const string DecisionStatusName = "Decision status";
    public const string CorrectionTypeName = "Correction type";
    public const string CycleYearName = "Cycle year";
    public const string CycleMonthName = "Cycle month";
    public const string KeyStageName = "Key stage";
    public const string SurnameCypmdName = "Surname (CYPMD)";
    public const string ForenameCypmdName = "Forename (CYPMD)";
    public const string SexName = "Sex";
    public const string CorrectionReason31Name = "Correction reason (31)";
    public const string ReasonForRemovalName = "Reason for removal";
    public const string CypmdName = "CYPMD_ID";
    public const string RulesEngineOutcomeKeyName = "RulesEngineOutcomeKey";
    public const string RulesEngineMatchedRuleId = "RulesEngineMatchedRuleId";

    /// <summary>
    /// Gets all field names as a list.
    /// </summary>
    public static List<string> GetAllFieldNames()
    {
        return new ()
        {
            TypeOfOrganisationName,
            DfeEstablishmentNumberName,
            UpnName,
            SchoolUrnName,
            LdsMatchedPupilIdName,
            DateOfBirthCypmdName,
            AdmissionDateName,
            DecisionStatusName,
            CorrectionTypeName,
            CycleYearName,
            CycleMonthName,
            KeyStageName,
            SurnameCypmdName,
            ForenameCypmdName,
            SexName,
            CorrectionReason31Name,
            ReasonForRemovalName,
            CypmdName,
            RulesEngineOutcomeKeyName,
            RulesEngineMatchedRuleId
        };
    }
}