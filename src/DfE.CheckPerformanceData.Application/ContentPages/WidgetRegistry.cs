using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// The single catalogue of placeable content widgets. The editor reads it to build its palette and to
// seed a new widget's props; the renderer and import path read it to reject unknown types. Adding a
// widget = one entry here plus its render/edit partials (Web), no schema change.
public static class WidgetRegistry
{
    public static IReadOnlyList<WidgetDefinition> All { get; } =
    [
        new("heading", "Heading", ContributesToNav: true, """{"level":2,"text":""}"""),
        new("richtext", "Rich text", ContributesToNav: false, """{"html":""}"""),
        new("divider", "Divider", ContributesToNav: false, "{}"),
        new("card", "Card", ContributesToNav: false, """{"title":"","body":"","href":""}"""),
        new("summarylist", "Summary list", ContributesToNav: false, """{"rows":[]}"""),
        new("published", "Published callout", ContributesToNav: false, """{"text":""}"""),
        new("search",    "Search",            ContributesToNav: false, """{"label":"Search","placeholder":"","action":"/search","buttonText":"Search","scope":""}"""),
        new("results",   "Search results",    ContributesToNav: false, """{"scope":"","emptyText":"No results found."}"""),
        new("pagenav",   "Page navigation",   ContributesToNav: false, """{"mode":"headings","childrenParentPath":"","showSearch":false,"searchPath":"","searchLabel":"Search"}""")
    ];

    private static readonly Dictionary<string, WidgetDefinition> ByType =
        All.ToDictionary(w => w.Type, StringComparer.Ordinal);

    public static bool IsKnown(string type) => ByType.ContainsKey(type);

    public static WidgetDefinition? Get(string type) => ByType.GetValueOrDefault(type);

    // A freshly parsed props object — never a shared instance, since a JsonObject can only be parented
    // into one tree.
    public static JsonObject CreateDefaultProps(string type) =>
        JsonNode.Parse(Get(type)?.DefaultPropsJson ?? "{}")!.AsObject();
}
