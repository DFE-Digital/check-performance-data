namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// One row of the Remove learners file, already in LDS shape (spec column order is decided by
/// EgressColumnSets, not by property order). The trailing identifiers are CYPMD's own bookkeeping
/// and never reach the file.
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
    string ReferenceNumber);

/// <summary>One row of the New learners file. Column set FLAGGED for verification against LDS spec v2.4 (see EgressColumnSets).</summary>
public sealed record NewLearnerRow(
    string CorrectionId,
    string CorrectionType,
    string KeyStage,
    string LocalAuthority,
    string EstablishmentNumber,
    string Surname,
    string MiddleName,
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
    string SenStatus,
    Guid ChangeRequestId,
    long? TicketId,
    string ReferenceNumber);
