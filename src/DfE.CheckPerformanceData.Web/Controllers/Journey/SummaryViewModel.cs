using DfE.CheckPerformanceData.Web.QuestionFlow;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class SummaryViewModel
{
    public Guid WindowId { get; init; }
    public required List<SummaryRow> Rows { get; init; }
    public required string BackPageId { get; init; }
    public string? DebugJson { get; init; }
}

public sealed class SummaryRow(JourneyPage page, Question question, QuestionAnswer? answer, string resolvedTitle)
{
    public JourneyPage Page { get; } = page;
    public Question Question { get; } = question;
    public QuestionAnswer? Answer { get; } = answer;
    public string ResolvedTitle { get; } = resolvedTitle;

    public string DisplayAnswer => Question.Type switch
    {
        QuestionType.Date when Answer?.DateValue is { } d =>
            $"{d.Day:D2}/{d.Month:D2}/{d.Year}",
        QuestionType.FileUpload when Answer?.FileValues is { Count: > 0 } files =>
            string.Join(", ", files.Select(f => $"{f.OriginalFileName} ({f.PageCount} {(f.PageCount == 1 ? "page" : "pages")})")),
        QuestionType.Radio when Answer?.TextValue is { } v =>
            Question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v,
        _ => Answer?.TextValue ?? string.Empty
    };
}
