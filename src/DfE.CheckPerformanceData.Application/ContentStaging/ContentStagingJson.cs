using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Serialisation options for content-staging bundles. Indented + camelCase for a readable,
// portable, diff-friendly file; enums as strings so the import mode survives hand inspection.
public static class ContentStagingJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ContentBundle bundle) =>
        JsonSerializer.Serialize(bundle, Options);

    public static ContentBundle? Deserialize(string json) =>
        JsonSerializer.Deserialize<ContentBundle>(json, Options);
}
