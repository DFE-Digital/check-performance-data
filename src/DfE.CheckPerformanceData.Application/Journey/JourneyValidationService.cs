using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

// Pass maxEvidencePages > 0 to the DI registration to re-enable the page-count limit.
public sealed class JourneyValidationService(
    IEnumerable<IFormatValidator>? formatValidators = null,
    int maxEvidencePages = 0,
    TimeProvider? timeProvider = null) : IJourneyValidationService
{
    private readonly IReadOnlyList<IFormatValidator> _formatValidators =
        formatValidators?.ToList() ?? [];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Date rules compare against the UK calendar date, not UTC: between midnight and 1am BST the
    // two disagree, and "today" to a school means today in England. Duplicated from
    // Web/Extensions/LondonTime.cs because Application cannot reference Web; that file documents
    // why the IANA id resolves on both the Linux deploy target and Windows.
    private static readonly TimeZoneInfo UkZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

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

    public IReadOnlyList<DateFieldViolation> ValidatePageDates(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, string pupilName)
    {
        // Each rule set owns a disjoint set of pages: the EAL page has its own cross-field rules
        // (PageDateRules), and the six removal pages share the future-date rule
        // (RemovalJourneyDateRules). A page can belong to only one, and neither runs on a page
        // the other owns — the wording (and the whole rule) differ.
        if (string.Equals(page.Id, PageDateRules.EalDetailsPageId, StringComparison.Ordinal))
        {
            DateOnly? Answered(string questionId) =>
                answers.TryGetValue(questionId, out var answer) ? answer.DateValue?.ToDateOnly() : null;

            return PageDateRules.Evaluate(
                Answered(PageDateRules.StartedAtSchool),
                Answered(PageDateRules.FirstEnglishSchool),
                Answered(PageDateRules.ArrivedInEngland),
                UkToday(),
                pupilName);
        }

        if (RemovalJourneyDateRules.AppliesToPage(page.Id))
            return RemovalJourneyDateRules.EvaluateFutureDates(page, answers, UkToday(), pupilName);

        return [];
    }

    private DateOnly UkToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, UkZone));

    public string? ValidateAnswer(Question question, QuestionAnswer answer, string resolvedTitle, string? resolvedValidationFailure = null)
    {
        // The six removal journeys want the question's own "Enter the date …" wording for every
        // invalid-date failure, not the generic messages. Scoped to the removal date question
        // ids so the excluded EAL page and any generic date question keep their messages; the
        // blank case still honours resolvedValidationFailure as it always has.
        var scopedDateFailure = resolvedValidationFailure is not null &&
            RemovalJourneyDateRules.RemovalDateQuestionIds.Contains(question.Id)
            ? resolvedValidationFailure
            : null;

        var baseError = question.Type switch
        {
            QuestionType.Date when answer.DateValue is not { Day: > 0, Month: > 0, Year: > 0 }
                => scopedDateFailure ?? resolvedValidationFailure ?? $"{resolvedTitle} is required",
            // A 4-digit year is required (GOV.UK date input pattern): "26" would otherwise be
            // accepted as the literal year 0026. Checked before IsValidDate, which must only
            // see years DateTime.DaysInMonth accepts (1-9999).
            QuestionType.Date when answer.DateValue!.Year is < 1000 or > 9999
                => scopedDateFailure ?? $"{resolvedTitle} must include a 4-digit year",
            QuestionType.Date when !IsValidDate(answer.DateValue!)
                => scopedDateFailure ?? $"{resolvedTitle} must be a real date",
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

    public EvidenceValidationResult? ValidateEvidencePage(JourneyPage page, RequestState journey, string pupilName,
        IReadOnlySet<string>? conditionallyOptionalQuestionIds = null)
    {
        bool IsOptional(Question q) =>
            q.Optional || (conditionallyOptionalQuestionIds?.Contains(q.Id) ?? false);

        var messages = new List<string>();

        foreach (var question in page.Questions)
        {
            if (question.Type == QuestionType.FileUpload)
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var existing);
                if (!IsOptional(question) && (existing?.FileValues ?? []).Count == 0)
                    messages.Add("Upload at least one file before continuing");
            }
            else
            {
                journey.QuestionAnswers.TryGetValue(question.Id, out var answer);
                answer ??= new QuestionAnswer();
                if (!IsOptional(question) || IsAnswered(question, answer))
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
