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

        return GraftInForest(forest, pageTree, AdminNavKeys.ContentPages, MapPageNode);
    }

    // Grafts a pruned page tree under the ContentBlocks node — same shape as the
    // pages tree but each node links to /admin/content-blocks?page=/{path}. Pages
    // are pruned to only those that own blocks or contain descendants that do, so
    // the tree matches the flat list on /admin/content-blocks.
    public static IReadOnlyList<AdminNavNodeViewModel> GraftContentBlockPageTree(
        IReadOnlyList<AdminNavNodeViewModel> forest,
        IReadOnlyList<PageTreeNode> pageTree,
        ISet<string> pagesWithBlocks)
    {
        if (pageTree is null || pageTree.Count == 0 || pagesWithBlocks.Count == 0)
            return forest;

        var pruned = PrunePagesWithBlocks(pageTree, pagesWithBlocks);
        if (pruned.Count == 0)
            return forest;

        return GraftInForest(forest, pruned, AdminNavKeys.ContentBlocks,
            (node, parentKey) => MapContentBlockPageNode(node, parentKey, pagesWithBlocks));
    }

    private static IReadOnlyList<AdminNavNodeViewModel> GraftInForest(
        IReadOnlyList<AdminNavNodeViewModel> forest,
        IReadOnlyList<PageTreeNode> pageTree,
        string targetKey,
        Func<PageTreeNode, string, AdminNavNodeViewModel> mapper)
    {
        var result = new List<AdminNavNodeViewModel>(forest.Count);
        var changed = false;
        foreach (var node in forest)
        {
            var grafted = GraftInNode(node, pageTree, targetKey, mapper);
            result.Add(grafted);
            if (!ReferenceEquals(grafted, node)) changed = true;
        }
        return changed ? result : forest;
    }

    private static AdminNavNodeViewModel GraftInNode(
        AdminNavNodeViewModel node,
        IReadOnlyList<PageTreeNode> pageTree,
        string targetKey,
        Func<PageTreeNode, string, AdminNavNodeViewModel> mapper)
    {
        if (node.Entry.Key == targetKey)
        {
            return new AdminNavNodeViewModel
            {
                Entry = node.Entry,
                Children = pageTree.Select(n => mapper(n, targetKey)).ToList(),
            };
        }

        var newChildren = GraftInForest(node.Children, pageTree, targetKey, mapper);
        if (ReferenceEquals(newChildren, node.Children))
            return node;

        return new AdminNavNodeViewModel
        {
            Entry = node.Entry,
            Children = newChildren,
        };
    }

    private static IReadOnlyList<PageTreeNode> PrunePagesWithBlocks(
        IReadOnlyList<PageTreeNode> nodes,
        ISet<string> pagesWithBlocks)
    {
        var kept = new List<PageTreeNode>();
        foreach (var node in nodes)
        {
            var prunedChildren = PrunePagesWithBlocks(node.Children, pagesWithBlocks);
            var hasOwnBlocks = pagesWithBlocks.Contains(node.Path);
            if (hasOwnBlocks || prunedChildren.Count > 0)
            {
                kept.Add(node with { Children = prunedChildren });
            }
        }
        return kept;
    }

    private static AdminNavNodeViewModel MapContentBlockPageNode(
        PageTreeNode node,
        string parentNavKey,
        ISet<string> pagesWithBlocks)
    {
        var entry = new ContentBlockPageNavEntry(
            node.Id, node.Title, node.PageType, node.Path,
            pagesWithBlocks.Contains(node.Path), parentNavKey);
        return new AdminNavNodeViewModel
        {
            Entry = entry,
            Children = node.Children
                .Select(c => MapContentBlockPageNode(c, entry.Key, pagesWithBlocks))
                .ToList(),
        };
    }

    private static AdminNavNodeViewModel MapPageNode(PageTreeNode node, string parentNavKey)
    {
        var entry = new PageTreeNavEntry(node.Id, node.Title, node.PageType, node.Path, node.HasLiveVersion, parentNavKey);
        return new AdminNavNodeViewModel
        {
            Entry = entry,
            Children = node.Children.Count == 0
                ? []
                : node.Children.Select(c => MapPageNode(c, entry.Key)).ToList(),
        };
    }

    // Walks the forest depth-first and yields every entry it contains — both DI-registered
    // nodes and synthetic page-tree nodes grafted at render time. The layout passes this to
    // ResolveActiveKey so /admin/pages/{id} matches the synthetic page-{id} entry (whose Url
    // is longer than /admin/pages) rather than the parent content-pages entry. When grafting
    // failed (exception) the forest contains only DI-registered entries, giving the same
    // result as passing AdminNavEntries directly.
    public static IEnumerable<IAdminNavEntry> CollectEntries(IReadOnlyList<AdminNavNodeViewModel> forest)
    {
        foreach (var node in forest)
        {
            yield return node.Entry;
            foreach (var entry in CollectEntries(node.Children))
                yield return entry;
        }
    }
}
