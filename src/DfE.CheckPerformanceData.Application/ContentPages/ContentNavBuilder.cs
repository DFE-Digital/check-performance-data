namespace DfE.CheckPerformanceData.Application.ContentPages;

// Builds a page's left-hand nav by walking the content tree (depth-first, document order) for
// Heading widgets. H2 → top-level item; H3 → nested under the most recent H2 (an H3 before any H2
// falls back to top-level). Other widgets and other heading levels (H1, H4–H6) do not contribute.
public static class ContentNavBuilder
{
    public static IReadOnlyList<ContentNavItem> Build(IReadOnlyList<ContentNode> tree)
    {
        var top = new List<MutableItem>();
        MutableItem? currentH2 = null;

        foreach (var heading in Walk(tree))
        {
            var (level, text) = HeadingProps(heading);
            if (text is null || heading.Anchor is null) continue;

            var item = new MutableItem(text, $"#{heading.Anchor}");
            if (level == 2)
            {
                top.Add(item);
                currentH2 = item;
            }
            else if (level == 3)
            {
                if (currentH2 is null) top.Add(item);
                else currentH2.Children.Add(item);
            }
            // Other levels do not appear in the nav.
        }

        return top.Select(Freeze).ToList();
    }

    // Heading widgets in document order (descending into nested regions).
    private static IEnumerable<WidgetNode> Walk(IReadOnlyList<ContentNode> nodes) =>
        ContentTreeWalker.AllWidgets(nodes).Where(w => w.Type == "heading");

    private static (int? Level, string? Text) HeadingProps(WidgetNode heading)
    {
        var props = heading.Props;
        if (props is null) return (null, null);
        var level = props.TryGetPropertyValue("level", out var l) ? (int?)l : null;
        var text = props.TryGetPropertyValue("text", out var t) ? (string?)t : null;
        return (level, text);
    }

    private static ContentNavItem Freeze(MutableItem item) =>
        new(item.Text, item.Href, item.Children.Select(Freeze).ToList());

    private sealed record MutableItem(string Text, string Href)
    {
        public List<MutableItem> Children { get; } = [];
    }
}
