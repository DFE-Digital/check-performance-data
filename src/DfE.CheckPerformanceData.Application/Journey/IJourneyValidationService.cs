using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IJourneyValidationService
{
    string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle);
    string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles);
    string GenerateReference(CheckingWindowType? windowType);
}
