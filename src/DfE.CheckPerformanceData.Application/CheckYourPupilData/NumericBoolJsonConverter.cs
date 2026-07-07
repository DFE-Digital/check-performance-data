using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Binds a boolean that the supplier expresses numerically. The pupil schema sends
/// <c>NEWMOBILE</c> as <c>0</c>/<c>1</c>; <see cref="JsonNumberHandling.AllowReadingFromString"/>
/// does not cover <see cref="bool"/>, and System.Text.Json will not bind a JSON number to a
/// bool, so this converter bridges the gap. It reads native <c>true</c>/<c>false</c>, the
/// numbers <c>0</c>/<c>1</c>, and their quoted forms (<c>"0"</c>/<c>"1"</c>/<c>"true"</c>/<c>"false"</c>),
/// and writes <c>0</c>/<c>1</c> so the dev seeder's output mirrors the supplier.
/// </summary>
public sealed class NumericBoolJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetInt32() != 0,
            JsonTokenType.String => ParseString(reader.GetString()),
            _ => throw new JsonException($"Cannot convert token '{reader.TokenType}' to a boolean.")
        };

    private static bool ParseString(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "y" => true,
        "0" or "false" or "no" or "n" or "" or null => false,
        _ => throw new JsonException($"Cannot convert '{value}' to a boolean.")
    };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value ? 1 : 0);
}
