using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// One page an editor can tick in a widget's page picker. Depth is the page's level in the tree
// (0 for a top-level page), used to indent the list.
public sealed record PageScopeOption(Guid Id, string Path, string Label, int Depth);

// The site's pages, flattened into tree order for the widget editor's page picker.
public sealed class PageScopePickerModel
{
    // Key the editor view reads the picker's pages from.
    public const string ViewDataKey = "PageScopePicker";

    public static PageScopePickerModel Empty { get; } = new([]);

    public IReadOnlyList<PageScopeOption> Options { get; }

    private PageScopePickerModel(IReadOnlyList<PageScopeOption> options) => Options = options;

    // The checkboxes for one widget. Every page posts its id. A page is ticked when the widget
    // stores its id, or, for a widget saved when pages were stored by path, when its path is in
    // the widget's path scope. Anything stored that no longer matches a page follows the pages,
    // ticked, so saving the form cannot silently drop it and the editor can see it is stale.
    public IReadOnlyList<PageScopePickerItem> ItemsFor(string? pageIds, string? scopePaths)
    {
        var ids = ScopePageIds.Parse(pageIds);
        var paths = SearchScope.Parse(scopePaths);

        var items = Options
            .Select(o => new PageScopePickerItem(
                o.Id.ToString(), o.Label, o.Path, o.Depth,
                Ticked: ids.Contains(o.Id) || paths.Contains(o.Path, StringComparer.OrdinalIgnoreCase),
                Note: null))
            .ToList();

        var known = Options.Select(o => o.Id).ToHashSet();
        items.AddRange(ids
            .Where(id => !known.Contains(id))
            .Select(id => new PageScopePickerItem(
                id.ToString(), id.ToString(), null, 0, Ticked: true, Note: "page no longer exists")));

        items.AddRange(paths
            .Where(p => !Options.Any(o => string.Equals(o.Path, p, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PageScopePickerItem(
                p, "/" + p, null, 0, Ticked: true, Note: "no page at this path")));

        return items;
    }

    // Parents come before their children; siblings follow their menu order.
    public static PageScopePickerModel From(IReadOnlyCollection<PageNodeTreeItemDto> tree)
    {
        var known = tree.Select(n => n.Id).ToHashSet();
        var childrenOf = tree
            .GroupBy(n => n.ParentId is { } p && known.Contains(p) ? p : Guid.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ThenBy(n => n.Title).ToList());

        var options = new List<PageScopeOption>(tree.Count);
        void Walk(Guid parent, int depth)
        {
            if (!childrenOf.TryGetValue(parent, out var nodes)) return;
            foreach (var node in nodes)
            {
                options.Add(new PageScopeOption(node.Id, node.Path, node.DisplayName, depth));
                Walk(node.Id, depth + 1);
            }
        }
        Walk(Guid.Empty, 0);
        return new PageScopePickerModel(options);
    }
}

// What one page picker renders: the id prefix that keeps its ids unique among the widgets on the
// page, the pages the widget currently holds (by path, as older widgets do, and by id), and the
// wording around the checkboxes.
public sealed record PageScopePickerField(string IdPrefix, string? Scope, string? PageIds, string Legend, string Hint);

// One checkbox in a page picker. Value is what it posts: a page id, or a stale path kept from an
// older widget. Path is the page's path, shown as a hint, and null for an entry with no page.
// Note says why an entry has no page.
public sealed record PageScopePickerItem(string Value, string Label, string? Path, int Depth, bool Ticked, string? Note);
