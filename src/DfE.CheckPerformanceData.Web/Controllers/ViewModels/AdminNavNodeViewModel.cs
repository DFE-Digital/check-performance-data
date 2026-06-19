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
}
