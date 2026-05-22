using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

/// <summary>
/// Tolerant <see cref="DecisionType"/> reader. A producer that stamps an
/// unrecognised value (or none at all) on the wire would otherwise crash the
/// worker before the rules engine ever ran. Since <c>DecisionType</c> is
/// informational on the wire — the engine makes the authoritative decision —
/// any unknown string is mapped to <see cref="DecisionType.Scrutiny"/>
/// (the safe default).
/// </summary>
public sealed class TolerantDecisionTypeConverter : JsonConverter<DecisionType>
{
    public override DecisionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return DecisionType.Scrutiny;
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return Enum.TryParse<DecisionType>(raw, ignoreCase: true, out var parsed)
                ? parsed
                : DecisionType.Scrutiny;
        }
        if (reader.TokenType == JsonTokenType.Number)
        {
            var n = reader.GetInt32();
            return Enum.IsDefined(typeof(DecisionType), n) ? (DecisionType)n : DecisionType.Scrutiny;
        }
        return DecisionType.Scrutiny;
    }

    public override void Write(Utf8JsonWriter writer, DecisionType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
