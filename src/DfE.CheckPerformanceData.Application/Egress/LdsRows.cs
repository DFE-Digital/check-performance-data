namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// One row of the Remove learners file, already in LDS shape (spec column order is decided by
/// EgressColumnSets, not by property order). The trailing identifiers are CYPMD's own bookkeeping
/// and never reach the file. The init-only properties are the key-stage-specific columns of
/// LDS_CYPMD_Data specification v2.4: Year_Group is in KS4 files only, Removal_Year_0..2 in 16-19
/// files only; a row for another key stage leaves them blank and its column set never emits them.
/// </summary>
public sealed record RemoveLearnerRow(
    string CorrectionId,
    string CorrectionType,
    string CorrectionReason,
    string KeyStage,
    string EstablishmentNumber,
    string Surname,
    string Forename,
    string Sex,
    string DateOfBirth,
    string CycleYear,
    string CycleMonth,
    string LocalAuthority,
    string LearnerId,
    Guid ChangeRequestId,
    long? TicketId,
    string ReferenceNumber)
{
    /// <summary>KS4 only, and only for year-group-change removals: the year group the pupil moves to.</summary>
    public string YearGroup { get; init; } = string.Empty;
    /// <summary>16-19 only: TRUE/FALSE for the current academic year; blank when the journey did not ask.</summary>
    public string RemovalYear0 { get; init; } = string.Empty;
    /// <summary>16-19 only: TRUE/FALSE for the previous academic year.</summary>
    public string RemovalYear1 { get; init; } = string.Empty;
    /// <summary>16-19 only: TRUE/FALSE for the academic year before that.</summary>
    public string RemovalYear2 { get; init; } = string.Empty;
}

/// <summary>
/// One row of the New learners file (LDS_CYPMD_Data specification v2.4 "New Learner" sheet).
/// Property names are CYPMD's; the CSV headings (Post_Code, URN, …) live in EgressColumnSets.
/// Middle_Name is struck through in v2.4 and SEN status is not in the spec, so neither is here.
/// </summary>
public sealed record NewLearnerRow(
    string CorrectionId,
    string CorrectionType,
    string KeyStage,
    string LocalAuthority,
    string EstablishmentNumber,
    string Surname,
    string Forename,
    string Sex,
    string DateOfBirth,
    string AdmissionDate,
    string Postcode,
    string CycleYear,
    string CycleMonth,
    string SchoolUrn,
    string Uln,
    string Upn,
    string LearnerId,
    string YearGroup,
    Guid ChangeRequestId,
    long? TicketId,
    string ReferenceNumber);
