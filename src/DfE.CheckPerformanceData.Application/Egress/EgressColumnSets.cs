namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>One column of an LDS file: the exact heading and how a row yields its value.</summary>
public sealed record EgressColumn<T>(string Header, Func<T, string> Value);

/// <summary>
/// THE definition of each LDS file: heading text, order and count (AB#292610 says headings must
/// match the spec exactly, with no extra columns). Change a file's shape here and nowhere else.
///
/// RemoveLearners is the column list in docs/spikes/data-egress-spike.md, verbatim.
///
/// NewLearners is DERIVED (FLAGGED): the LDS_CYPMD_Data specification v2.4 workbook was not
/// available when this shipped, so the set comes from the tactical Zendesk "New Learner Output"
/// report plus AB#292610's rules — LA and establishment split from the 7-digit number, UPN
/// between ULN and the matched LDS ref, no combined 7-digit field — and SEN status because the
/// Add journey captures it as an LDS-bound value (docs/add-pupil-journey.md). Verify against the
/// spec and correct here; EgressColumnSetsTests pins whatever is decided.
/// </summary>
public static class EgressColumnSets
{
    public static readonly IReadOnlyList<EgressColumn<RemoveLearnerRow>> RemoveLearners =
    [
        new("Correction_ID", r => r.CorrectionId),
        new("Correction_Type", r => r.CorrectionType),
        new("Correction_Reason", r => r.CorrectionReason),
        new("Key_Stage", r => r.KeyStage),
        new("Establishment_Number", r => r.EstablishmentNumber),
        new("Surname", r => r.Surname),
        new("Forename", r => r.Forename),
        new("Sex", r => r.Sex),
        new("Date_of_Birth", r => r.DateOfBirth),
        new("Cycle_Year", r => r.CycleYear),
        new("Cycle_Month", r => r.CycleMonth),
        new("Local_Authority", r => r.LocalAuthority),
        new("Learner_ID", r => r.LearnerId)
    ];

    public static readonly IReadOnlyList<EgressColumn<NewLearnerRow>> NewLearners =
    [
        new("Correction_ID", r => r.CorrectionId),
        new("Correction_Type", r => r.CorrectionType),
        new("Key_Stage", r => r.KeyStage),
        new("Local_Authority", r => r.LocalAuthority),
        new("Establishment_Number", r => r.EstablishmentNumber),
        new("Surname", r => r.Surname),
        new("Middle_Name", r => r.MiddleName),
        new("Forename", r => r.Forename),
        new("Sex", r => r.Sex),
        new("Date_of_Birth", r => r.DateOfBirth),
        new("Admission_Date", r => r.AdmissionDate),
        new("Postcode", r => r.Postcode),
        new("Cycle_Year", r => r.CycleYear),
        new("Cycle_Month", r => r.CycleMonth),
        new("School_URN", r => r.SchoolUrn),
        new("ULN", r => r.Uln),
        new("UPN", r => r.Upn),
        new("Learner_ID", r => r.LearnerId),
        new("Year_Group", r => r.YearGroup),
        new("SEN_Status", r => r.SenStatus)
    ];
}
