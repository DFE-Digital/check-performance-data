using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IJourneyValidationService
{
    int MaxEvidencePages { get; }
    bool IsAnswered(Question question, QuestionAnswer? answer);
    RequireAtLeastOneResult? ValidateRequireAtLeastOne(JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName);
    string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null);
    string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles);
    string GenerateReference(CheckingWindowType? windowType);
}
