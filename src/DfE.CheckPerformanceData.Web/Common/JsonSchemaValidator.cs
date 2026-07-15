using System.Text.Json;

namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// Lightweight validation that an uploaded file is a JSON schema document. This does not perform
/// full JSON Schema meta-schema validation (no schema library is referenced); it confirms the
/// content is well-formed JSON whose root is an object that looks like a schema — it either
/// declares a <c>$schema</c> keyword or contains a recognised schema keyword.
/// </summary>
public static class JsonSchemaValidator
{
    private static readonly string[] SchemaKeywords =
    [
        "$schema", "type", "properties", "$defs", "definitions", "allOf", "anyOf", "oneOf", "items"
    ];

    public static bool TryValidate(Stream content, out string? error)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The file is not a JSON schema. The root must be a JSON object.";
                return false;
            }

            foreach (string keyword in SchemaKeywords)
            {
                if (document.RootElement.TryGetProperty(keyword, out _))
                {
                    error = null;
                    return true;
                }
            }

            error = "The file does not look like a JSON schema.";
            return false;
        }
        catch (JsonException exception)
        {
            error = $"The file is not valid JSON: {exception.Message}";
            return false;
        }
    }
}
