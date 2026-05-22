using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.RulesEngine.Json;

/// <summary>
/// Reads a JSON literal (<c>"KS4"</c>, <c>true</c>, <c>402</c>, <c>"2025-01-16"</c>)
/// into a <see cref="FieldValue"/> primitive. The converter never produces
/// <see cref="FieldValue.Date"/> directly — JSON has no date type, so date
/// literals arrive as strings and are realised as <see cref="FieldValue.Date"/>
/// by the validator using the field catalogue.
///
/// Also handles writing literals back out (for diagnostics / round-tripping).
/// </summary>
public sealed class FieldValueJsonConverter : JsonConverter<FieldValue>
{
    public override FieldValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new FieldValue.Str(reader.GetString()!);
            case JsonTokenType.True:
            case JsonTokenType.False:
                return new FieldValue.Bool(reader.GetBoolean());
            case JsonTokenType.Number:
                return new FieldValue.Num(reader.GetDecimal());
            case JsonTokenType.Null:
                return FieldValue.Unknown.Instance;
            default:
                throw new JsonException(
                    $"Unsupported literal type {reader.TokenType}; expected string, bool, number, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FieldValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FieldValue.Str s:       writer.WriteStringValue(s.Value); break;
            case FieldValue.Bool b:      writer.WriteBooleanValue(b.Value); break;
            case FieldValue.Num n:       writer.WriteNumberValue(n.Value); break;
            case FieldValue.Date d:      writer.WriteStringValue(d.Value.ToString("yyyy-MM-dd")); break;
            case FieldValue.Unknown:     writer.WriteNullValue(); break;
            case FieldValue.Uncertain u: Write(writer, u.Inner, options); break;
            default: throw new JsonException($"Cannot serialise {value.GetType().Name}");
        }
    }
}
