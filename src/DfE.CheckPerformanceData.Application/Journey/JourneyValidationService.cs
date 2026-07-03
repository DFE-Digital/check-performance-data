using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

// Pass maxEvidencePages > 0 to the DI registration to re-enable the page-count limit.
public sealed class JourneyValidationService(
    IEnumerable<IFormatValidator>? formatValidators = null,
    int maxEvidencePages = 0) : IJourneyValidationService
{
    private readonly IReadOnlyList<IFormatValidator> _formatValidators =
        formatValidators?.ToList() ?? [];

    public int MaxEvidencePages => maxEvidencePages;

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

    public string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null)
    {
        var baseError = question.Type switch
        {
            QuestionType.Date when answer.DateValue is not { Day: > 0, Month: > 0, Year: > 0 }
                => resolvedValidationFailure ?? $"{resolvedTitle} is required",
            // A 4-digit year is required (GOV.UK date input pattern): "26" would otherwise be
            // accepted as the literal year 0026. Checked before IsValidDate, which must only
            // see years DateTime.DaysInMonth accepts (1-9999).
            QuestionType.Date when answer.DateValue!.Year is < 1000 or > 9999
                => $"{resolvedTitle} must include a 4-digit year",
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

        return baseError ?? ValidateFormat(question, answer.TextValue);
    }

    // Runs a named IFormatValidator against a present text value. Skips when the
    // question has no validator, the value is empty (emptiness is the required
    // rule's concern), or the named validator is not registered (fail open — a
    // bad config name must not block every submission).
    private string? ValidateFormat(Question question, string? textValue)
    {
        if (string.IsNullOrWhiteSpace(question.Validator) || string.IsNullOrWhiteSpace(textValue))
            return null;

        var validator = _formatValidators.FirstOrDefault(v => v.Name == question.Validator);
        return validator is not null && !validator.IsValid(textValue) ? validator.FailureMessage : null;
    }

    private static bool IsValidDate(DateAnswer d) =>
        d.Month is >= 1 and <= 12 &&
        d.Day >= 1 &&
        d.Day <= DateTime.DaysInMonth(d.Year, d.Month);

    public string? ValidateFileUpload(string fileName, int newPageCount, IReadOnlyList<FileAnswer> existingFiles)
    {
        if (maxEvidencePages <= 0) return null;

        var currentTotal = existingFiles.Sum(f => f.PageCount);
        if (currentTotal + newPageCount <= maxEvidencePages) return null;

        return $"'{fileName}' has {newPageCount} {(newPageCount == 1 ? "page" : "pages")}. " +
            $"Adding it would bring the total to {currentTotal + newPageCount} pages, " +
            $"which exceeds the {maxEvidencePages}-page limit.";
    }

    public string GenerateReference(CheckingWindowType? windowType)
    {
        var type = windowType?.ToString() ?? "Unknown";
        var uniqueId = Guid.NewGuid().ToString("N")[..7].ToUpper();
        return $"CYPMD_{type}_{uniqueId}";
    }

    public EvidenceValidationResult? ValidateEvidencePage(JourneyPage page, RequestState journey, string pupilName)
    {
        var messages = new List<string>();

        foreach (var question in page.Questions)
        {
            if (question.Type == QuestionType.FileUpload)
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var existing);
                if (!question.Optional && (existing?.FileValues ?? []).Count == 0)
                    messages.Add("Upload at least one file before continuing");
            }
            else
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var answer);
                answer ??= new QuestionAnswer();
                if (!question.Optional || IsAnswered(question, answer))
                {
                    // Mirror PagePost: surface the config's validationFailure (with {pupilName}
                    // resolved) so the edit page shows the same message as the live journey.
                    var resolvedValidationFailure = question.ValidationFailure is not null
                        ? JourneyTemplate.Resolve(question.ValidationFailure, pupilName) : null;
                    var error = ValidateAnswer(question, answer,
                        JourneyTemplate.Resolve(question.Title, pupilName), resolvedValidationFailure);
                    if (error is not null) messages.Add(error);
                }
            }
        }

        var atLeastOne = ValidateRequireAtLeastOne(page, journey.QuestionAnswers, pupilName);
        if (atLeastOne is not null) messages.Add(atLeastOne.SummaryMessage);

        return messages.Count == 0 ? null : new EvidenceValidationResult { Messages = messages };
    }
}
