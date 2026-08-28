using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads a JSON string, <c>null</c>, number or boolean into a <see cref="string"/>, mapping
/// <c>null</c> to <see cref="string.Empty"/>.
///
/// Every field on <see cref="StudentResultRecord"/> is a string because the values are opaque
/// codes (grades such as <c>*2</c> and <c>24F</c>, QANs such as <c>6037116X</c>) that must never be
/// coerced to a number. But the ingestion step converts supplier CSVs, and CSV-to-JSON converters
/// routinely emit numeric-looking columns unquoted — <c>"GRADE": 5</c> rather than <c>"GRADE": "5"</c>.
/// The default string converter throws on a number token, so a single unquoted column would fail
/// the whole file. <c>JsonNumberHandling.AllowReadingFromString</c> does not help: it relaxes the
/// opposite direction (string token into a numeric property).
///
/// <see cref="HandleNull"/> is <c>true</c> because System.Text.Json otherwise short-circuits a null
/// token for reference types and never calls the converter — the same reason
/// <c>NullToEmptyStringJsonConverter</c> overrides it for the pupil schema.
/// </summary>
public sealed class TolerantStringJsonConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => string.Empty,
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            // The raw token text, so 5 reads as "5" and not "5.0" via a round-trip through double.
            JsonTokenType.Number => Encoding.UTF8.GetString(
                reader.HasValueSequence ? BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => throw new JsonException($"Cannot read a {reader.TokenType} token as a string.")
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
