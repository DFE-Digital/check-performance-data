using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Typed reads over a widget's free-form props bag. Each widget partial pulls the handful of values it
// needs by name; a missing or null prop yields null rather than throwing, so a half-filled widget
// still renders.
public static class WidgetProps
{
    public static string? GetString(this WidgetNode widget, string key) =>
        widget.Props is { } p && p.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;

    public static int? GetInt(this WidgetNode widget, string key) =>
        widget.Props is { } p && p.TryGetPropertyValue(key, out var node) && node is not null
            ? node.GetValue<int>()
            : null;

    public static JsonArray? GetArray(this WidgetNode widget, string key) =>
        widget.Props is { } p && p.TryGetPropertyValue(key, out var node) ? node as JsonArray : null;

    // Safe boolean read. Tolerates the value being stored as a real bool (from a JSON default)
    // OR the string "true" / "false" (from a form post). Returns null if the prop is absent or
    // the value can't be read as either — never throws.
    public static bool? GetBool(this WidgetNode widget, string key)
    {
        if (widget.Props is not { } p) return null;
        if (!p.TryGetPropertyValue(key, out var node) || node is null) return null;
        try
        {
            if (node is JsonValue jv)
            {
                if (jv.TryGetValue<bool>(out var b)) return b;
                if (jv.TryGetValue<string>(out var s))
                    return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { /* fall through to null */ }
        return null;
    }
}
