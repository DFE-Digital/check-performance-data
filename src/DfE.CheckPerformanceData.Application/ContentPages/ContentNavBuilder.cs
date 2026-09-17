namespace DfE.CheckPerformanceData.Application.ContentPages;

// Builds a page's contents nav by walking the content tree (depth-first, document order) for
// Heading widgets.
//
// Which heading levels appear is the author's choice — any combination of H1 to H6. The chosen
// levels form the hierarchy in the order they are chosen rather than by their absolute numbers,
// so an author who picks H2 and H4 because the page does not use H3 gets H4 nested directly
// under H2, and the H3s are ignored. That is the whole point of choosing: the nav should follow
// the structure of this page, not the structure the levels imply in the abstract.
//
// Levels fall back rather than being dropped: a heading whose parent level has not appeared yet
// attaches to the nearest chosen level above it that has, and one with nothing above it at all
// becomes top-level. Authors skip levels, and dropping those headings would leave sections of
// the page the nav cannot reach, which is the job it exists to do.
public static class ContentNavBuilder
{
    // What a page gets when nobody has chosen: the two levels a contents list is usually made of.
    public static readonly IReadOnlyList<int> DefaultLevels = [2, 3];

    public static IReadOnlyList<ContentNavItem> Build(
        IReadOnlyList<ContentNode> tree,
        IReadOnlyCollection<int>? levels = null)
    {
        // Sorted and de-duplicated: the rank of a level is its position in this list, and that
        // is what decides nesting.
        var chosen = (levels ?? DefaultLevels)
            .Where(level => level is >= 1 and <= 6)
            .Distinct()
            .OrderBy(level => level)
            .ToList();

        if (chosen.Count == 0) return [];

        var top = new List<MutableItem>();

        // The most recent item at each rank. A heading attaches to the nearest non-null entry
        // above its own rank; everything below its rank is cleared, so a later sibling cannot
        // collect the children of the branch that just ended.
        var openAtRank = new MutableItem?[chosen.Count];

        foreach (var heading in Walk(tree))
        {
            var (level, text) = HeadingProps(heading);
            if (text is null || heading.Anchor is null || level is null) continue;

            var rank = chosen.IndexOf(level.Value);
            if (rank < 0) continue;

            var item = new MutableItem(text, $"#{heading.Anchor}");

            MutableItem? parent = null;
            for (var above = rank - 1; above >= 0 && parent is null; above--)
                parent = openAtRank[above];

            if (parent is null) top.Add(item);
            else parent.Children.Add(item);

            openAtRank[rank] = item;
            for (var below = rank + 1; below < openAtRank.Length; below++)
                openAtRank[below] = null;
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
