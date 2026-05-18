namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionAnswer
{
    public string? TextValue { get; set; }
    public DateAnswer? DateValue { get; set; }
    public List<FileAnswer>? FileValues { get; set; }
}
