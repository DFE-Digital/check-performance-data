namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? SubLabel { get; init; }
    public string? NextPageId { get; init; }
}
