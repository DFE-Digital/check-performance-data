using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Turns the editor's flat form fields into a widget's typed props, using the registry's default props
// as the schema: only known keys are taken, numeric defaults coerce their field to a number (a bad
// value keeps the default), and the summary-list "rows" textarea ("key|value" per line) is parsed
// into the structured array the widget expects.
public static class WidgetPropsBuilder
{
    public static JsonObject Build(string type, IReadOnlyDictionary<string, string?> fields)
    {
        var props = WidgetRegistry.CreateDefaultProps(type);

        foreach (var key in props.Select(kvp => kvp.Key).ToList())
        {
            if (props[key] is JsonArray)
            {
                if (key == "rows" && fields.TryGetValue("rows", out var rowsText))
                    props[key] = ParseRows(rowsText);
                continue;
            }

            if (fields.TryGetValue(key, out var raw))
                props[key] = Coerce(props[key], raw);
        }

        return props;
    }

    private static JsonNode? Coerce(JsonNode? template, string? raw)
    {
        // A numeric prop keeps its default when the field isn't a valid number.
        if (template is JsonValue value && value.TryGetValue<int>(out _))
            return int.TryParse(raw, out var number) ? JsonValue.Create(number) : template;

        return JsonValue.Create(raw ?? string.Empty);
    }

    // "key|value" per line; blank and pipe-less lines are skipped.
    private static JsonArray ParseRows(string? text)
    {
        var rows = new JsonArray();
        if (string.IsNullOrWhiteSpace(text)) return rows;

        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split('|', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.Length == 0 && value.Length == 0) continue;

            rows.Add(new JsonObject { ["key"] = key, ["value"] = value });
        }

        return rows;
    }
}
