using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Reads a JSON <c>null</c> string as <see cref="string.Empty"/>. The supplier pupil schema
/// declares several string fields as nullable (e.g. <c>CYPMD_ID</c>, <c>SEX</c>, <c>ETHNIC</c>),
/// but downstream code calls string methods such as <c>StartsWith</c> on them during search, so a
/// null would <see cref="NullReferenceException"/>. Registered on the pupil
/// <see cref="JsonSerializerOptions"/> so every <see cref="string"/> property is guaranteed
/// non-null. <see cref="HandleNull"/> is overridden to <c>true</c> because System.Text.Json
/// otherwise short-circuits a null token for reference types and never calls the converter.
/// </summary>
public sealed class NullToEmptyStringJsonConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
