using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class PageViewModel
{
    public Guid WindowId { get; init; }
    public required JourneyPage Page { get; init; }
    public required Dictionary<string, QuestionAnswer> Answers { get; init; }
    public required IReadOnlyList<QuestionPartialModel> QuestionModels { get; init; }
    public string? BackPageId { get; init; }
    public bool BackPageIsPupilSearch { get; init; }
    public bool FromSummary { get; init; }
    public string PupilName { get; init; } = string.Empty;
    public string? ContentKey { get; init; }
    public string? UploadError { get; init; }
    public string? AtLeastOneError { get; init; }

    public string? ResolvedTitle => string.IsNullOrEmpty(Page.Title) ? null : ResolveTemplate(Page.Title);
    public bool IsSingleQuestion => Page.Questions.Count == 1;

    // The browser <title> (and therefore analytics) must never contain the pupil name.
    // Prefer the author-supplied sanitised PageTitle; otherwise fall back to a
    // pupil-name-free version of the page title, then the single question's title.
    public string PageTitle =>
        Page.PageTitle
        ?? (string.IsNullOrEmpty(Page.Title) ? null : JourneyTemplate.Strip(Page.Title))
        ?? (Page.Questions.Count > 0 ? JourneyTemplate.Strip(Page.Questions[0].Title) : string.Empty);
    public bool HasErrors => QuestionModels.Any(q => q.Error is not null) || UploadError is not null || AtLeastOneError is not null;

    public IReadOnlyList<QuestionPartialModel> FileUploadModels =>
        QuestionModels.Where(q => q.Question.Type == QuestionType.FileUpload).ToList();

    public IReadOnlyList<QuestionPartialModel> NonFileUploadModels =>
        QuestionModels.Where(q => q.Question.Type != QuestionType.FileUpload).ToList();

    public QuestionAnswer? GetAnswer(string questionId) =>
        Answers.TryGetValue(questionId, out var a) ? a : null;

    public string ResolveTemplate(string template) =>
        template.Replace("{pupilName}", PupilName, StringComparison.OrdinalIgnoreCase);
}
