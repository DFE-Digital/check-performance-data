using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Admin.Nav;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Recursive node for the left-hand admin nav tree. Unlike AdminNavGroupViewModel (the
// flat two-level model the landing page still uses), this supports arbitrary depth: each
// node wraps a single entry and carries its own ordered child nodes, so the renderer can
// recurse for the 3-4 level rules-engine sub-tree. Children are pre-sorted by Order.
public sealed class AdminNavNodeViewModel
{
    public required IAdminNavEntry Entry { get; init; }
    public IReadOnlyList<AdminNavNodeViewModel> Children { get; init; } = [];

    // Builds the full forest from a flat entry list: roots are entries with a null
    // ParentKey; each node's children are entries whose ParentKey equals its Key.
    public static IReadOnlyList<AdminNavNodeViewModel> BuildForest(IEnumerable<IAdminNavEntry> entries)
    {
        var all = entries.ToList();

        List<AdminNavNodeViewModel> ChildrenOf(string? parentKey) => all
            .Where(e => e.ParentKey == parentKey)
            .OrderBy(e => e.Order)
            .Select(e => new AdminNavNodeViewModel
            {
                Entry = e,
                Children = ChildrenOf(e.Key),
            })
            .ToList();

        return ChildrenOf(null);
    }

    // Grafts a live page tree under the ContentPages node in the given forest.
    // Returns a new forest with the ContentPages node's Children replaced by the
    // page tree mapped to AdminNavNodeViewModels using PageTreeNavEntry. Other nodes
    // are returned by reference (unchanged). Defensive: null/empty page tree returns
    // the forest unchanged.
    public static IReadOnlyList<AdminNavNodeViewModel> GraftPageTree(
        IReadOnlyList<AdminNavNodeViewModel> forest,
        IReadOnlyList<PageTreeNode> pageTree)
    {
        if (pageTree is null || pageTree.Count == 0)
            return forest;

        return GraftInForest(forest, pageTree);
    }

    private static IReadOnlyList<AdminNavNodeViewModel> GraftInForest(
        IReadOnlyList<AdminNavNodeViewModel> forest,
        IReadOnlyList<PageTreeNode> pageTree)
    {
        var result = new List<AdminNavNodeViewModel>(forest.Count);
        var changed = false;
        foreach (var node in forest)
        {
            var grafted = GraftInNode(node, pageTree);
            result.Add(grafted);
            if (!ReferenceEquals(grafted, node)) changed = true;
        }
        return changed ? result : forest;
    }

    private static AdminNavNodeViewModel GraftInNode(
        AdminNavNodeViewModel node,
        IReadOnlyList<PageTreeNode> pageTree)
    {
        if (node.Entry.Key == AdminNavKeys.ContentPages)
        {
            return new AdminNavNodeViewModel
            {
                Entry = node.Entry,
                Children = MapPageNodes(pageTree, AdminNavKeys.ContentPages),
            };
        }

        var newChildren = GraftInForest(node.Children, pageTree);
        if (ReferenceEquals(newChildren, node.Children))
            return node;

        return new AdminNavNodeViewModel
        {
            Entry = node.Entry,
            Children = newChildren,
        };
    }

    private static AdminNavNodeViewModel MapPageNode(PageTreeNode node, string parentNavKey)
    {
        var entry = new PageTreeNavEntry(node.Id, node.Title, node.PageType, node.HasLiveVersion, parentNavKey);
        return new AdminNavNodeViewModel
        {
            Entry = entry,
            Children = MapPageNodes(node.Children, entry.Key),
        };
    }

    private static IReadOnlyList<AdminNavNodeViewModel> MapPageNodes(
        IReadOnlyList<PageTreeNode> nodes,
        string parentNavKey)
    {
        if (nodes is null || nodes.Count == 0)
            return [];

        return nodes.Select(n => MapPageNode(n, parentNavKey)).ToList();
    }
}
