namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionAnswer
{
    public string? TextValue { get; set; }
    public DateAnswer? DateValue { get; set; }
    public List<FileAnswer>? FileValues { get; set; }
}
