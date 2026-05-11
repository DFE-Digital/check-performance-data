namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class Question
{
    public required string Id { get; init; }
    public required QuestionType Type { get; init; }
    public required string Title { get; init; }
    public string? Hint { get; init; }
    public bool ContentKey { get; init; }
    public List<QuestionOption>? Options { get; init; }
}
