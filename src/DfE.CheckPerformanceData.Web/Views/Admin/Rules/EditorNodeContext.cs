using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Web.Views.Admin.Rules;

/// <summary>
/// View context for the recursive predicate editor partial. Carries the node being
/// rendered, its index in the flat list (for input names), the full node list (to find
/// children and assign child indices) and the field dropdown data.
/// </summary>
public sealed class EditorNodeContext
{
    public required IReadOnlyList<PredicateNodeForm> Nodes { get; init; }
    public required IReadOnlyList<string> AllFields { get; init; }
    public required PredicateNodeForm Node { get; init; }

    public int Index => IndexOf(Node.Id);

    private int IndexOf(int id)
    {
        for (var k = 0; k < Nodes.Count; k++)
        {
            if (Nodes[k].Id == id) return k;
        }
        return -1;
    }

    public IReadOnlyList<PredicateNodeForm> ChildrenOf(int parentId) =>
        Nodes.Where(n => n.ParentId == parentId).ToList();

    public EditorNodeContext Recurse(PredicateNodeForm child) => new()
    {
        Nodes = Nodes, AllFields = AllFields, Node = child
    };
}
