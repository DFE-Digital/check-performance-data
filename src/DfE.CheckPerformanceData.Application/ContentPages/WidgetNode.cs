using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// A content component. <see cref="Type"/> selects the widget contract (registry); <see cref="Props"/>
// holds its free-form properties, deserialised into a typed model by that contract. <see cref="Anchor"/>
// is a server-generated id for nav/deep-linking (heading widgets set it).
public sealed class WidgetNode : ContentNode
{
    public required string Type { get; init; }
    // Anchor and Props are settable: the editor re-anchors headings and replaces props in place after
    // an edit. Type never changes (a widget is replaced, not retyped), so it stays init-only.
    public string? Anchor { get; set; }
    public JsonObject? Props { get; set; }
}
