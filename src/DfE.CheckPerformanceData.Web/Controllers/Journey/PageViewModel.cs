using DfE.CheckPerformanceData.Web.QuestionFlow;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class PageViewModel
{
    public Guid WindowId { get; init; }
    public required JourneyPage Page { get; init; }
    public required Dictionary<string, QuestionAnswer> Answers { get; init; }
    public string? BackPageId { get; init; }
    public bool FromSummary { get; init; }
    public string PupilName { get; init; } = string.Empty;
    public string? ContentKey { get; init; }

    public QuestionAnswer? GetAnswer(string questionId) =>
        Answers.TryGetValue(questionId, out var a) ? a : null;

    public string ResolveTemplate(string template) =>
        template.Replace("{pupilName}", PupilName, StringComparison.OrdinalIgnoreCase);
}
