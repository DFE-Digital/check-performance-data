using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IJourneyValidationService
{
    int MaxEvidencePages { get; }
    bool IsAnswered(Question question, QuestionAnswer? answer);
    /// <summary>
    /// The page's "answer at least one of these" rule. <paramref name="requireAtLeastOneActive"/>
    /// is <see cref="IQuestionOptionalityService.IsRequireAtLeastOneActive"/> for the current
    /// journey — pass it on any page that may set <see cref="JourneyPage.RequireAtLeastOneWhen"/>.
    /// Null falls back to the page's raw flag.
    /// </summary>
    RequireAtLeastOneResult? ValidateRequireAtLeastOne(JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName,
        bool? requireAtLeastOneActive = null);

    /// <summary>
    /// Runs the <c>dateRules</c> of each <c>Date</c> question on the page. Returns at most one
    /// message for each question, in page order.
    ///
    /// <paramref name="answers"/> must span the whole journey with the page's posted answers
    /// on top: a rule can compare a date on this page with a date on another page.
    /// <paramref name="ctx"/> is given to the date facts that the rules name.
    /// </summary>
    IReadOnlyList<DateFieldViolation> ValidatePageDates(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, JourneyConditionContext ctx, string pupilName);

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
    /// <param name="heldInOtherFiles">The same result in the student's other results files
    /// (<see cref="ResultsEnquiry.HeldResults.InOtherFiles"/>). A chosen grade that one of them holds
    /// is refused with a message that names that file. Null or empty skips the rule.</param>
    /// <param name="windowType">The type of the journey's window. It decides how the message names the
    /// file (<see cref="ResultsEnquiry.ResultsSources.SchoolLabelFor"/>). Null gives the label.</param>
    string? ValidateGradeSelect(
        Question question,
        QuestionAnswer? answer,
        ResultsEnquiry.GradeReference? reference,
        string? currentGrade,
        string? resolvedValidationFailure = null,
        IReadOnlyList<ResultsEnquiry.ResultRecord>? heldInOtherFiles = null,
        CheckingWindowType? windowType = null);
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
    /// AB#304900: refuses another file once the upload already holds
    /// <see cref="EvidenceUploadLimits.MaxFiles"/>. Pass the files the question holds now.
    /// Returns the user-facing error, or null when one more may be added.
    /// </summary>
    string? ValidateFileCount(IReadOnlyList<FileAnswer> existingFiles);

    /// <summary>
    /// Rejects a file whose name is already used by any evidence file in the request
    /// (AB#296081). Case-insensitive on <see cref="FileAnswer.OriginalFileName"/>.
    /// Returns the user-facing error, or null when the name is new.
    /// </summary>
    string? ValidateDuplicateFileName(string fileName, IReadOnlyList<FileAnswer> existingFiles);

    string GenerateReference(CheckingWindowType? windowType);

    /// <summary>
    /// Makes the reference of a results enquiry: <c>CYPMD_{key stage}_RE_{7 hex}</c>. The <c>RE</c>
    /// part tells support staff that the reference is for an enquiry, not for an amendment.
    /// </summary>
    /// <param name="windowType">
    /// The type of the enquiry's window. <c>Post16</c> gives the key stage part <c>16to19</c> and
    /// <c>KS4Autumn</c> gives <c>KS4</c>. Another type gives its enum name. Null gives <c>Unknown</c>.
    /// </param>
    /// <returns>A new reference. Its last part is seven random hexadecimal characters in upper case.</returns>
    string GenerateEnquiryReference(CheckingWindowType? windowType);
    EvidenceValidationResult? ValidateEvidencePage(JourneyPage page, RequestState journey, string pupilName,
        IReadOnlySet<string>? conditionallyOptionalQuestionIds = null,
        bool? requireAtLeastOneActive = null);
}
