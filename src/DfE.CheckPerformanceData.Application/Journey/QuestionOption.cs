using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? SubLabel { get; init; }
    public string? NextPageId { get; init; }

    /// <summary>
    /// Names of the <see cref="IJourneyCondition"/>s that must ALL evaluate true
    /// for this option to be shown/selectable. JSON accepts a bare string (legacy
    /// single-condition form) or an array; see <see cref="VisibleWhenJsonConverter"/>.
    /// </summary>
    [JsonConverter(typeof(VisibleWhenJsonConverter))]
    public IReadOnlyList<string>? VisibleWhen { get; init; }
}
