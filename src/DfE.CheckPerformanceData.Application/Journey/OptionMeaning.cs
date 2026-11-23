using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// What an option means. An option declares its meaning in the flow file (<c>"meaning"</c>), and
/// code tests the meaning, never the option value. See docs/journey-config-first.md, pattern 5.
/// Add a meaning only when code reads it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OptionMeaning>))]
public enum OptionMeaning
{
    Yes,
    No
}
