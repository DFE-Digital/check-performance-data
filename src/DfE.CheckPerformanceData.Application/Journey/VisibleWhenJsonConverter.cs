using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Reads <c>visibleWhen</c> as either a bare condition name (the original
/// single-condition contract, still present in deployed blobs) or an array of
/// names. Always writes the array form.
/// </summary>
public sealed class VisibleWhenJsonConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return [reader.GetString()!];

        return JsonSerializer.Deserialize<List<string>>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, (IEnumerable<string>)value, options);
}
