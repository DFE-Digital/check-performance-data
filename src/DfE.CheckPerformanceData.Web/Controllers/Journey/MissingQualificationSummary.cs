namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The check-answers rows for a missing-qualification enquiry (AB#297848). Mirrors
/// <see cref="ResultsEnquirySummary"/>'s shape: leads with the establishment, key stage and enquiry
/// type, then identifies the student and the qualification, and finishes with the claimed grade.
///
/// AO and QAN change through the qualification-search page (they are resolved together, server-side
/// — see <c>JourneyController.QualificationSearchPost</c>); syllabus code, award date, grade and NCN
/// each change through the details page; the qualification title has no Change link of its own,
/// since it is derived from the QAN rather than a separate answer.
/// </summary>
public sealed class MissingQualificationSummary
{
    public required string DfeNumber { get; init; }
    public required string KeyStageLabel { get; init; }
    public required string StudentName { get; init; }

    /// <summary>True when the enquiry covers a whole cohort, which adds the count row and changes
    /// how the student row is labelled — the student is then an example, not the subject.</summary>
    public required bool IsCohortWide { get; init; }

    /// <summary>The answer to "how many students", shown only on the cohort branch.</summary>
    public string? CohortCount { get; init; }

    public string? CypmdId { get; init; }
    public string? AwardingOrganisation { get; init; }
    public string? Qan { get; init; }
    public string? QualificationTitle { get; init; }
    public string? SyllabusCode { get; init; }

    /// <summary>Already display-formatted (DateAnswer.ToDisplayString()).</summary>
    public string? AwardDate { get; init; }

    public string? Ncn { get; init; }
    public string? GradeAchieved { get; init; }
    public string? AdditionalInformation { get; init; }

    public required string QualificationPageId { get; init; }
    public required string DetailsPageId { get; init; }
    public required string AdditionalInformationPageId { get; init; }

    public IReadOnlyList<SummaryLine> Lines
    {
        get
        {
            var lines = new List<SummaryLine>
            {
                Fixed("DfE number", DfeNumber),
                Fixed("Key stage", KeyStageLabel),
                Fixed("Enquiry type", "Missing qualification")
            };

            if (IsCohortWide)
                lines.Add(Fixed("Number of students in affected cohort", CohortCount ?? string.Empty));

            // The label is how the summary conveys the cohort answer — there is no separate yes/no
            // row on the Figma screens.
            lines.Add(Fixed(IsCohortWide ? "Name of a student in cohort" : "Name of student", StudentName));

            lines.Add(Fixed("CYPMD ID", CypmdId ?? string.Empty));

            lines.Add(new SummaryLine(
                "Awarding Organisation (AO) name", AwardingOrganisation ?? string.Empty,
                QualificationPageId, true, "Awarding Organisation (AO) name"));
            lines.Add(new SummaryLine(
                "Qualification number (QAN)", Qan ?? string.Empty,
                QualificationPageId, true, "Qualification number (QAN)"));
            lines.Add(Fixed("Qualification name and subject", QualificationTitle ?? string.Empty));

            lines.Add(new SummaryLine(
                "Syllabus code", SyllabusCode ?? string.Empty, DetailsPageId, true, "syllabus code"));
            lines.Add(new SummaryLine(
                "Award date", AwardDate ?? string.Empty, DetailsPageId, true, "award date"));
            lines.Add(new SummaryLine(
                "NCN", Ncn ?? string.Empty, DetailsPageId, true, "NCN"));
            lines.Add(new SummaryLine(
                "Grade achieved", GradeAchieved ?? string.Empty, DetailsPageId, true, "grade achieved"));

            lines.Add(new SummaryLine(
                "Additional information", AdditionalInformation ?? string.Empty,
                AdditionalInformationPageId, true, "additional information"));

            return lines;
        }
    }

    private static SummaryLine Fixed(string key, string value) => new(key, value, null, false, null);
}
