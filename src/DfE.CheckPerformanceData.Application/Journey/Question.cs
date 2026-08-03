using System.Text.Json.Serialization;

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

    /// <summary>
    /// Optional name of an <c>IFormatValidator</c> applied to this question's
    /// answer once it is non-empty (e.g. <c>"DfeNumber"</c>). An unregistered
    /// name is ignored.
    /// </summary>
    public string? Validator { get; init; }

    /// <summary>
    /// Names of the <see cref="IJourneyCondition"/>s that must ALL evaluate true
    /// for this question's answer to become optional (overriding <see cref="Optional"/>
    /// = false). An unregistered name leaves the question mandatory (fail closed).
    /// JSON accepts a bare string or an array, like <see cref="QuestionOption.VisibleWhen"/>.
    /// </summary>
    [JsonConverter(typeof(VisibleWhenJsonConverter))]
    public IReadOnlyList<string>? OptionalWhen { get; init; }
}
