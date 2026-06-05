namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class Question
{
    public required string Id { get; init; }
    public required QuestionType Type { get; init; }
    public required string Title { get; init; }
    public string? SummaryTitle { get; init; }
    public bool Optional { get; init; }
    public string? Hint { get; init; }
    public bool ContentKey { get; init; }
    public bool UseAsRequestType { get; init; }
    public int? CharacterLimit { get; init; }
    public List<QuestionOption>? Options { get; init; }
    public string? DataSource { get; init; }
    public string? QuestionHelpTitle { get; init; }
    public string? QuestionHelpText { get; init; }
    public string? ValidationFailure { get; init; }
}
