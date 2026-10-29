using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? SubLabel { get; init; }
    public string? NextPageId { get; init; }

    /// <summary>
    /// Conditions that must ALL hold for this option to be shown and selectable: condition
    /// names or answer tests (<see cref="JourneyConditionRef"/>). JSON accepts one entry or an
    /// array; see <see cref="VisibleWhenJsonConverter"/>. An undecided entry hides the option.
    /// </summary>
    [JsonConverter(typeof(VisibleWhenJsonConverter))]
    public IReadOnlyList<JourneyConditionRef>? VisibleWhen { get; init; }
}
