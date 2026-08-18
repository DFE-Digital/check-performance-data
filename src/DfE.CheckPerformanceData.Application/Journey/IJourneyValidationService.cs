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
    /// question. Empty for every page without rules, which is all of them bar the EAL details
    /// page today.
    /// </summary>
    IReadOnlyList<DateFieldViolation> ValidatePageDates(JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName);

    string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null);
    string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles);

    /// <summary>
    /// Rejects a file whose name is already used by any evidence file in the request
    /// (AB#296081). Case-insensitive on <see cref="FileAnswer.OriginalFileName"/>.
    /// Returns the user-facing error, or null when the name is new.
    /// </summary>
    string? ValidateDuplicateFileName(string fileName, IReadOnlyList<FileAnswer> existingFiles);

    string GenerateReference(CheckingWindowType? windowType);
    EvidenceValidationResult? ValidateEvidencePage(JourneyPage page, RequestState journey, string pupilName,
        IReadOnlySet<string>? conditionallyOptionalQuestionIds = null);
}
