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
    public string PageTitle => ResolvedTitle ?? (QuestionModels.Count > 0 ? QuestionModels[0].ResolvedTitle : string.Empty);
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
