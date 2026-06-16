namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Render-time wrapper passed to the recursive _AdminNavNode partial: the node to render,
// whether it is the last sibling (for the tree connector styling), and the currently
// active key so the partial can mark/expand the active branch at any depth.
public sealed record AdminNavNodePartialModel(
    AdminNavNodeViewModel Node,
    bool IsLast,
    string? ActiveKey)
{
    public bool SubtreeContainsActive => Contains(Node, ActiveKey);

    private static bool Contains(AdminNavNodeViewModel node, string? activeKey)
    {
        if (node.Entry.Key == activeKey) return true;
        foreach (var child in node.Children)
        {
            if (Contains(child, activeKey)) return true;
        }
        return false;
    }
}
