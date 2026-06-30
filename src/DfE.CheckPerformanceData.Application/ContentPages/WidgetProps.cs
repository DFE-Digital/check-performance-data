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
}
