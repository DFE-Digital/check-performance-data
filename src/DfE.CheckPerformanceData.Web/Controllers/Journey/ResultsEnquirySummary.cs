using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The check-answers rows for a results enquiry (AB#296648).
///
/// An enquiry's summary is not an amendment's with a few substitutions — it leads with the
/// establishment, key stage and enquiry type, then identifies the student and the exam result, and
/// finishes with what the school says the grade should be. So it owns its own row set rather than
/// bending the amendment one, and <see cref="SummaryViewModel.Lines"/> picks between them.
///
/// Only the revised grade and the comments carry a Change link, per the Figma screens. Changing the
/// student or the result is deliberately not a summary action: a different result invalidates the
/// grade chosen for it, so that route goes back through the journey where the engine clears the
/// dependent answer. The result-does-not-belong variant (AB#298704) omits the revised-grade row
/// entirely, since that journey has no grade step.
/// </summary>
public sealed class ResultsEnquirySummary
{
    public required string DfeNumber { get; init; }
    public required string KeyStageLabel { get; init; }
    public required string EnquiryTypeLabel { get; init; }
    public required string StudentName { get; init; }

    /// <summary>True when the enquiry covers a whole cohort, which adds the count row and changes
    /// how the student row is labelled — the student is then an example, not the subject.</summary>
    public required bool IsCohortWide { get; init; }

    /// <summary>The answer to "how many students", shown only on the cohort branch.</summary>
    public string? CohortCount { get; init; }

    public StudentResultRecord? Result { get; init; }

    /// <summary>False for enquiry kinds with no revised-grade step (AB#298704): the row and its
    /// Change link are omitted entirely rather than rendered empty.</summary>
    public required bool ShowRevisedGrade { get; init; }

    public string? RevisedGrade { get; init; }
    public string? RevisedGradePageId { get; init; }
    public string? AdditionalInformation { get; init; }
    public string? AdditionalInformationPageId { get; init; }

    public IReadOnlyList<SummaryLine> Lines
    {
        get
        {
            var lines = new List<SummaryLine>
            {
                Fixed("DfE number", DfeNumber),
                Fixed("Key stage", KeyStageLabel),
                Fixed("Enquiry type", EnquiryTypeLabel)
            };

            if (IsCohortWide)
                lines.Add(Fixed("Number of students in affected cohort", CohortCount ?? string.Empty));

            // The label is how the summary conveys the cohort answer — there is no separate yes/no
            // row on the Figma screens.
            lines.Add(Fixed(IsCohortWide ? "Name of a student in cohort" : "Name of student", StudentName));

            lines.Add(Fixed("CYPMD ID", Result?.CypmdId ?? string.Empty));
            lines.Add(Fixed("Qualification number (QAN)", Result?.Qan ?? string.Empty));
            lines.Add(Fixed("Qualification name and subject", Result?.QualificationName ?? string.Empty));
            lines.Add(Fixed("Session", Result?.Session ?? string.Empty));
            lines.Add(Fixed("Current grade", Result?.Grade ?? string.Empty));

            if (ShowRevisedGrade)
                lines.Add(new SummaryLine(
                    "Revised grade", RevisedGrade ?? string.Empty, RevisedGradePageId, true, "revised grade"));
            lines.Add(new SummaryLine(
                "Additional information", AdditionalInformation ?? string.Empty,
                AdditionalInformationPageId, true, "additional information"));

            return lines;
        }
    }

    private static SummaryLine Fixed(string key, string value) => new(key, value, null, false, null);
}
