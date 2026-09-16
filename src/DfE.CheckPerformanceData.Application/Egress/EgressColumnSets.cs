using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>One column of an LDS file: the exact heading and how a row yields its value.</summary>
public sealed record EgressColumn<T>(string Header, Func<T, string> Value);

/// <summary>
/// THE definition of each LDS file: heading text, order and count (AB#292610 says headings must
/// match the spec exactly, with no extra columns). Both sets are LDS_CYPMD_Data specification v2.4
/// read top to bottom ("New Learner" and "Remove Learner" sheets); the spec marks some attributes
/// N/A for a key stage, so the *For(windowType) methods are what callers use — the base lists are
/// the columns every key stage shares. Change a file's shape here and nowhere else.
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

    // v2.4 "New Learner": Middle_Name (row 15) is struck through — "CYPMD will not be sending this
    // field from June 2026" — so it is not emitted at all. There is no SEN attribute in the spec.
    public static readonly IReadOnlyList<EgressColumn<NewLearnerRow>> NewLearners =
    [
        new("Correction_ID", r => r.CorrectionId),
        new("Correction_Type", r => r.CorrectionType),
        new("Key_Stage", r => r.KeyStage),
        new("Establishment_Number", r => r.EstablishmentNumber),
        new("Surname", r => r.Surname),
        new("Forename", r => r.Forename),
        new("Sex", r => r.Sex),
        new("Date_of_Birth", r => r.DateOfBirth),
        new("Admission_Date", r => r.AdmissionDate),
        new("Post_Code", r => r.Postcode),
        new("Cycle_Year", r => r.CycleYear),
        new("Cycle_Month", r => r.CycleMonth),
        new("Local_Authority", r => r.LocalAuthority),
        new("URN", r => r.SchoolUrn),
        new("ULN", r => r.Uln),
        new("UPN", r => r.Upn),
        new("Learner_ID", r => r.LearnerId),
        new("Year_Group", r => r.YearGroup)
    ];

    // v2.4 "New Learner" rows 29-32: 16-18 only (N/A for KS2/KS4). CYPMD has no Post16 Add journey,
    // so nothing can populate these yet; the headings must still be present and the cells blank
    // (all four are NULL-able). A future Post16 Add journey fills them from its own answers.
    private static readonly IReadOnlyList<EgressColumn<NewLearnerRow>> Post16NewLearnerColumns =
    [
        new("Attendance_Year_0", _ => string.Empty),
        new("Attendance_Year_1", _ => string.Empty),
        new("Attendance_Year_2", _ => string.Empty),
        new("KS4_Year", _ => string.Empty)
    ];

    public static IReadOnlyList<EgressColumn<NewLearnerRow>> NewLearnersFor(CheckingWindowType windowType) => windowType switch
    {
        CheckingWindowType.KS2 or CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn => NewLearners,
        CheckingWindowType.Post16 => [.. NewLearners, .. Post16NewLearnerColumns],
        _ => throw Unmapped(windowType)
    };

    private static ArgumentOutOfRangeException Unmapped(CheckingWindowType windowType) =>
        new(nameof(windowType), windowType, "This window type has no LDS column set. Add it to EgressColumnSets before egressing it.");
}
