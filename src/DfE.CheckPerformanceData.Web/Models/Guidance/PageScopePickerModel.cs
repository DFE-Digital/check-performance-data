using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// One page an editor can tick in a widget's page picker. Depth is the page's level in the tree
// (0 for a top-level page), used to indent the list.
public sealed record PageScopeOption(string Path, string Label, int Depth);

// The site's pages, flattened into tree order for the widget editor's page picker.
public sealed class PageScopePickerModel
{
    // Key the editor view reads the picker's pages from.
    public const string ViewDataKey = "PageScopePicker";

    public static PageScopePickerModel Empty { get; } = new([]);

    public IReadOnlyList<PageScopeOption> Options { get; }

    private PageScopePickerModel(IReadOnlyList<PageScopeOption> options) => Options = options;

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
                options.Add(new PageScopeOption(node.Path, node.DisplayName, depth));
                Walk(node.Id, depth + 1);
            }
        }
        Walk(Guid.Empty, 0);
        return new PageScopePickerModel(options);
    }
}

// What one page picker renders: the id prefix that keeps its ids unique among the widgets on the
// page, the scope the widget currently holds, and the wording around the checkboxes.
public sealed record PageScopePickerField(string IdPrefix, string? Scope, string Legend, string Hint);
