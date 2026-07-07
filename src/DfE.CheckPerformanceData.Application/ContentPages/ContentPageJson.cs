using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Serialisation for a page's content tree. camelCase + indented for a readable, diff-friendly
// stored document; null props/anchor omitted; the "kind" discriminator (declared on ContentNode)
// drives polymorphic round-tripping of regions and widgets.
public static class ContentPageJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IReadOnlyList<ContentNode> tree) =>
        JsonSerializer.Serialize(tree, Options);

    public static IReadOnlyList<ContentNode>? Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ContentNode>>(json, Options);
}
