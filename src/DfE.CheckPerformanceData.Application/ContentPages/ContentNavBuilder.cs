namespace DfE.CheckPerformanceData.Application.ContentPages;

// Builds a page's left-hand nav by walking the content tree (depth-first, document order) for
// Heading widgets. H2 → top-level item; H3 → nested under the most recent H2; H4 → nested under
// the most recent H3.
//
// Authors skip levels, so each level falls back to the nearest one that exists rather than being
// dropped: an H4 with no H3 above it attaches to the current H2, and a heading with nothing above
// it at all becomes top-level. Dropping it instead would leave a section of the page that the
// nav cannot reach, which is the whole job of this list.
//
// H1 is the page title and would duplicate the heading above the nav; H5 and H6 are below the
// depth a contents list stays readable at. Neither contributes.
public static class ContentNavBuilder
{
    public static IReadOnlyList<ContentNavItem> Build(IReadOnlyList<ContentNode> tree)
    {
        var top = new List<MutableItem>();
        MutableItem? currentH2 = null;
        MutableItem? currentH3 = null;

        foreach (var heading in Walk(tree))
        {
            var (level, text) = HeadingProps(heading);
            if (text is null || heading.Anchor is null) continue;

            var item = new MutableItem(text, $"#{heading.Anchor}");
            switch (level)
            {
                case 2:
                    top.Add(item);
                    currentH2 = item;
                    // A new section starts a fresh branch: without this reset an H4 under the
                    // new H2 would attach to the previous section's last H3.
                    currentH3 = null;
                    break;

                case 3:
                    if (currentH2 is null) top.Add(item);
                    else currentH2.Children.Add(item);
                    currentH3 = item;
                    break;

                case 4:
                    if (currentH3 is not null) currentH3.Children.Add(item);
                    else if (currentH2 is not null) currentH2.Children.Add(item);
                    else top.Add(item);
                    break;

                // Other levels do not appear in the nav.
            }
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
