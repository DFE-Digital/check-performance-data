using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure, in-place structural edits to the flat predicate-node list. Every editor
/// postback applies exactly one of these, then re-renders (no blob write).
/// </summary>
public static class BranchEditTransforms
{
    public static int NextId(IReadOnlyList<PredicateNodeForm> nodes) =>
        nodes.Count == 0 ? 1 : nodes.Max(n => n.Id) + 1;

    public static void AddCondition(List<PredicateNodeForm> nodes, int parentId) =>
        nodes.Add(new PredicateNodeForm
        {
            Id = NextId(nodes), ParentId = parentId,
            Kind = PredicateKind.FieldEq, Field = FirstField(), Operator = "eq", Value = ""
        });

    /// <summary>Appends an empty AllOf group under the parent; change the combinator afterwards via SetCombinator.</summary>
    public static void AddGroup(List<PredicateNodeForm> nodes, int parentId) =>
        nodes.Add(new PredicateNodeForm
        {
            Id = NextId(nodes), ParentId = parentId, Kind = PredicateKind.AllOf
        });

    public static void Remove(List<PredicateNodeForm> nodes, int id)
    {
        var doomed = Descendants(nodes, id);
        doomed.Add(id);
        nodes.RemoveAll(n => doomed.Contains(n.Id));
    }

    /// <summary>
    /// Wraps all selected nodes (which must be siblings) in a new composite of the
    /// given <paramref name="kind"/>. The new composite is inserted at the position
    /// of the first selected sibling so that sibling order is preserved.
    /// </summary>
    public static void GroupSelected(List<PredicateNodeForm> nodes, PredicateKind kind)
    {
        var selected = nodes.Where(n => n.Selected).ToList();
        if (selected.Count == 0) return;

        // All selected nodes must be siblings. The UI enforces this; a crafted POST might not.
        // Bail rather than silently corrupt the tree.
        var parentId = selected[0].ParentId;
        if (selected.Any(n => n.ParentId != parentId)) return;

        var group = new PredicateNodeForm { Id = NextId(nodes), ParentId = parentId, Kind = kind };

        var firstIndex = nodes.IndexOf(selected[0]);
        nodes.Insert(firstIndex, group);

        // 'selected' is a pre-materialised snapshot, so the Insert above does not affect this loop.
        foreach (var n in selected)
        {
            n.ParentId = group.Id;
            n.Selected = false;
        }
    }

    public static void Ungroup(List<PredicateNodeForm> nodes, int compositeId)
    {
        var composite = nodes.FirstOrDefault(n => n.Id == compositeId);
        if (composite is null) return;

        foreach (var child in nodes.Where(n => n.ParentId == compositeId))
        {
            child.ParentId = composite.ParentId;
        }
        nodes.Remove(composite);
    }

    public static void SetCombinator(List<PredicateNodeForm> nodes, int id, PredicateKind kind)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is not null) node.Kind = kind;
    }

    public static void SetField(List<PredicateNodeForm> nodes, int id, string newField)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is null) return;

        node.Field = newField;
        node.Value = "";
        node.Values = new List<string>();

        var allowed = LeafEditorOptions.OperatorTokensFor(newField).Select(o => o.Token).ToList();
        if (node.Operator is null || !allowed.Contains(node.Operator))
        {
            node.Operator = allowed.FirstOrDefault() ?? "eq";
        }
    }

    public static void AddValue(List<PredicateNodeForm> nodes, int id) =>
        nodes.FirstOrDefault(n => n.Id == id)?.Values.Add("");

    public static void RemoveValue(List<PredicateNodeForm> nodes, int id, int index)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is not null && index >= 0 && index < node.Values.Count) node.Values.RemoveAt(index);
    }

    /// <summary>
    /// Recursively collects the IDs of all descendants of the node with the given
    /// <paramref name="id"/>. The node itself is NOT included; the caller adds it.
    /// </summary>
    private static List<int> Descendants(IReadOnlyList<PredicateNodeForm> nodes, int id)
    {
        var result = new List<int>();
        var visited = new HashSet<int>();
        void Walk(int parentId)
        {
            foreach (var childId in nodes.Where(n => n.ParentId == parentId).Select(n => n.Id))
            {
                if (!visited.Add(childId)) continue; // already seen — guards against tampered ParentId cycles
                result.Add(childId);
                Walk(childId);
            }
        }
        Walk(id);
        return result;
    }

    private static string FirstField() => FieldCatalogue.All.Keys.First();
}
