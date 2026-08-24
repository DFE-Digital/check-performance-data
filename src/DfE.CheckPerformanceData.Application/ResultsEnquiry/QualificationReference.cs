namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// AB#297848: one qualification from the supplier's QualList reference data — the source of the
/// AO / QAN / grade / syllabus dropdowns on the missing-qualification enquiry. Grade order is
/// preserved from the source; a scale has a meaningful order that sorting would destroy.
/// </summary>
public sealed class QualificationReference
{
    public required string Qan { get; init; }
    public required string QualificationTitle { get; init; }
    public string AwardingOrganisation { get; init; } = string.Empty;
    public IReadOnlyList<string> Grades { get; init; } = [];

    /// <summary>
    /// The QAN's 16-19 syllabus codes from the SyllabusCodes export (joined on QUID = un-slashed
    /// QAN). Only 13 of 974 QANs have any — FLAGGED to the BA, since the syllabus field is
    /// required — and an empty list degrades exactly as the grade picker does for a missing QAN.
    /// </summary>
    public IReadOnlyList<SyllabusCode> SyllabusCodes { get; init; } = [];

    /// <summary>
    /// Adapts this record to the shape the grade picker already validates with. Every grade is a
    /// "pass" grade because the QualList export carries no pass/fail split and the missing grade
    /// is the user's claim, not a ranked correction.
    /// </summary>
    public GradeReference ToGradeReference() => new()
    {
        Qan = Qan,
        QualificationTitle = QualificationTitle,
        AwardingOrganisation = AwardingOrganisation,
        PassGrades = Grades,
        FailGrades = []
    };
}

/// <summary>
/// One syllabus (specification) code with its human-readable title — sibling codes often differ
/// only by specialism, so the title is what lets a user tell them apart.
/// </summary>
public sealed class SyllabusCode
{
    public required string Code { get; init; }
    public string Title { get; init; } = string.Empty;
}
