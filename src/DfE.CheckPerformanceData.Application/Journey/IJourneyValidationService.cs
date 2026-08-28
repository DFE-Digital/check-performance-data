using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IJourneyValidationService
{
    int MaxEvidencePages { get; }
    bool IsAnswered(Question question, QuestionAnswer? answer);
    RequireAtLeastOneResult? ValidateRequireAtLeastOne(JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName);

    /// <summary>
    /// Cross-field date rules for the page, if it has any. Returns at most one message per
    /// question. Empty for every page without rules.
    ///
    /// <paramref name="answers"/> must span the whole journey with the page's posted answers
    /// overlaid: the Add rules compare a date on this page against one entered on another.
    /// </summary>
    IReadOnlyList<DateFieldViolation> ValidatePageDates(JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName);

    string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null);

    /// <summary>
    /// The revised-grade rules for a <see cref="QuestionType.GradeSelect"/> question (AB#296648).
    ///
    /// A separate method rather than a parameter on <see cref="ValidateAnswer"/>: the rules need
    /// inputs no other question type has (the qualification's grade scale, and the grade the result
    /// currently holds), and the caller must resolve the scale asynchronously before validating —
    /// which <see cref="ValidateAnswer"/> deliberately stays clear of.
    /// </summary>
    /// <param name="reference">The QAN's grade scale, or null when the qualification is absent from
    /// the AODC reference data — in which case nothing can be submitted.</param>
    /// <param name="currentGrade">The grade the selected result holds, or null when unknown.</param>
    string? ValidateGradeSelect(
        Question question,
        QuestionAnswer? answer,
        ResultsEnquiry.GradeReference? reference,
        string? currentGrade,
        string? resolvedValidationFailure = null);
    /// <summary>
    /// AB#297848: membership validation for a select whose options come from server-side state
    /// (SyllabusSelect). Fails closed — blank, unknown, and nothing-to-offer all return the same
    /// message, so a forged value is indistinguishable from no selection. Ordinal comparison,
    /// matching the grade rules: codes are opaque and normalisation could accept a value the
    /// picker never rendered.
    /// </summary>
    string? ValidateOptionSelect(
        Question question, QuestionAnswer? answer,
        IReadOnlyList<string> allowedValues, string? resolvedValidationFailure = null);

    string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles);

    /// <summary>
    /// Rejects a file whose name is already used by any evidence file in the request
    /// (AB#296081). Case-insensitive on <see cref="FileAnswer.OriginalFileName"/>.
    /// Returns the user-facing error, or null when the name is new.
    /// </summary>
    string? ValidateDuplicateFileName(string fileName, IReadOnlyList<FileAnswer> existingFiles);

    string GenerateReference(CheckingWindowType? windowType);

    /// <summary>AB#296648: the reference for a 16-19 results enquiry, <c>CYPMD_16to19_RE_{7 hex}</c>.</summary>
    string GenerateEnquiryReference();
    EvidenceValidationResult? ValidateEvidencePage(JourneyPage page, RequestState journey, string pupilName,
        IReadOnlySet<string>? conditionallyOptionalQuestionIds = null);
}
