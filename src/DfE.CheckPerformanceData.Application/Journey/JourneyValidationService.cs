using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class JourneyValidationService : IJourneyValidationService
{
    private const int MaxTotalPages = 6;

    public int MaxEvidencePages => MaxTotalPages;

    public bool IsAnswered(Question question, QuestionAnswer? answer) =>
        question.Type switch
        {
            QuestionType.FileUpload => answer?.FileValues is { Count: > 0 },
            QuestionType.Date => answer?.DateValue is { Day: > 0, Month: > 0, Year: > 0 },
            _ => !string.IsNullOrWhiteSpace(answer?.TextValue)
        };

    public RequireAtLeastOneResult? ValidateRequireAtLeastOne(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName)
    {
        if (!page.RequireAtLeastOne) return null;

        var anyAnswered = page.Questions.Any(q =>
            IsAnswered(q, answers.TryGetValue(q.Id, out var a) ? a : null));
        if (anyAnswered) return null;

        var fieldErrors = page.Questions.ToDictionary(
            q => q.Id,
            q => q.Type == QuestionType.FileUpload
                ? "Upload at least one file"
                : JourneyTemplate.Resolve(q.Title, pupilName));

        return new RequireAtLeastOneResult("You must answer at least one of these questions", fieldErrors);
    }

    public string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null) =>
        question.Type switch
        {
            QuestionType.Date when answer.DateValue is not { Day: > 0, Month: > 0, Year: > 0 }
                => resolvedValidationFailure ?? $"{resolvedTitle} is required",
            QuestionType.Date when !IsValidDate(answer.DateValue!)
                => $"{resolvedTitle} must be a real date",
            QuestionType.TextArea when string.IsNullOrWhiteSpace(answer.TextValue)
                => resolvedValidationFailure ?? $"{resolvedTitle} is required",
            QuestionType.TextArea when question.CharacterLimit.HasValue && answer.TextValue!.Length > question.CharacterLimit.Value
                => $"{resolvedTitle} must be {question.CharacterLimit} characters or less",
            QuestionType.Date => null,
            _ when string.IsNullOrWhiteSpace(answer.TextValue)
                => resolvedValidationFailure ?? $"{resolvedTitle} is required",
            _ => null
        };

    private static bool IsValidDate(DateAnswer d) =>
        d.Month is >= 1 and <= 12 &&
        d.Day >= 1 &&
        d.Day <= DateTime.DaysInMonth(d.Year, d.Month);

    public string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles)
    {
        var currentTotal = existingFiles.Sum(f => f.PageCount);
        if (currentTotal + newPageCount <= MaxTotalPages) return null;

        return $"'{fileName}' has {newPageCount} {(newPageCount == 1 ? "page" : "pages")}. " +
            $"Adding it would bring the total to {currentTotal + newPageCount} pages, " +
            $"which exceeds the {MaxTotalPages}-page limit.";
    }

    public string GenerateReference(CheckingWindowType? windowType)
    {
        var type = windowType?.ToString() ?? "Unknown";
        var uniqueId = Guid.NewGuid().ToString("N")[..7].ToUpper();
        return $"CYPMD_{type}_{uniqueId}";
    }

}
