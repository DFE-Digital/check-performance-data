using DfE.CheckPerformanceData.Web.QuestionFlow;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class SummaryViewModel
{
    public Guid WindowId { get; init; }
    public required List<SummaryRow> Rows { get; init; }
    public required string BackQuestionId { get; init; }
}

public sealed class SummaryRow(Question question, QuestionAnswer? answer)
{
    public Question Question { get; } = question;
    public QuestionAnswer? Answer { get; } = answer;

    public string DisplayAnswer => Question.Type switch
    {
        QuestionType.Date when Answer?.DateValue is { } d =>
            $"{d.Day:D2}/{d.Month:D2}/{d.Year}",
        QuestionType.FileUpload when Answer?.FileValue is { } f =>
            f.OriginalFileName,
        QuestionType.Radio when Answer?.TextValue is { } v =>
            Question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v,
        _ => Answer?.TextValue ?? string.Empty
    };
}
