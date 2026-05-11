namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? NextPageId { get; init; }
}
