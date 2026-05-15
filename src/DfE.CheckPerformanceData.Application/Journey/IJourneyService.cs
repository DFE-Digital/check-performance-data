using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IJourneyService
{
    string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle);
    string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles);
    string GenerateReference(CheckingWindowType? windowType);
    RequestDocument BuildRequestDocument(JourneySubmissionContext context, QuestionFlowConfig config);
}
