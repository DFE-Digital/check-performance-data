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
    public const string DciRefCypmdName = "DCI Ref (CYPMD)";
    public const string AgeCypmdName = "Age (CYPMD)";
    public const string DecisionReasonApprovedName = "Decision Reason - Approved";
    public const string DecisionReasonRejectedName = "Decision Reason - Rejected";
    public const string CorrectionReason21Name = "Correction reason (21)";
    public const string LdsMatchedPupilId2Name = "LDS matched pupil ID 2";
    public const string PupilYearGroupCypmdName = "Pupil year group (CYPMD)";
    public const string AdditionalCommentsCypmdName = "Additional comments (CYPMD)";

    // Results enquiry ticket fields (#859).
    public const string CustomerNameCypmdName = "Customer Name (CYPMD)";
    public const string EmailAddressCypmdName = "Email address (CYPMD)";
    public const string SchoolNameCypmdName = "School/college name (CYPMD)";
    public const string DfeNumberCypmdName = "DfE Number (num) (CYPMD)";
    public const string SituationCypmdName = "Situation (CYPMD)";
    public const string GradedExamCypmdName = "Graded exam (CYPMD)";
    public const string TLevelExamsCypmdName = "T Level Exams (CYPMD)";
    public const string WithdrawalQualificationCypmdName = "Withdrawal qualification (CYPMD)";
    public const string WholeCohortAffectedCypmdName = "Whole cohort affected? (CYPMD)";
    public const string NumberOfQualificationsCypmdName = "Number of missing/incorrect qualifications (CYPMD)";
    public const string AoNameCypmdName = "AO name (CYPMD)";
    public const string QanCypmdName = "QAN (CYPMD)";
    public const string QualificationNameCypmdName = "Qualification name and subject (CYPMD)";
    public const string SyllabusCodeCypmdName = "Syllabus code (CYPMD)";
    public const string AwardDateCypmdName = "Award date (CYPMD)";
    public const string NcnCypmdName = "National Centre Number (NCN) (CYPMD)";
    public const string CurrentGradeCypmdName = "Current grade (CYPMD)";
    public const string RevisedGradeCypmdName = "Revised/missing grade (CYPMD)";


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
            DciRefCypmdName,
            AgeCypmdName,
            DecisionReasonApprovedName,
            DecisionReasonRejectedName,
            CorrectionReason21Name,
            LdsMatchedPupilId2Name,
            PupilYearGroupCypmdName,
            AdditionalCommentsCypmdName,
            CustomerNameCypmdName,
            EmailAddressCypmdName,
            SchoolNameCypmdName,
            DfeNumberCypmdName,
            SituationCypmdName,
            GradedExamCypmdName,
            TLevelExamsCypmdName,
            WithdrawalQualificationCypmdName,
            WholeCohortAffectedCypmdName,
            NumberOfQualificationsCypmdName,
            AoNameCypmdName,
            QanCypmdName,
            QualificationNameCypmdName,
            SyllabusCodeCypmdName,
            AwardDateCypmdName,
            NcnCypmdName,
            CurrentGradeCypmdName,
            RevisedGradeCypmdName
        };
    }
}