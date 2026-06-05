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

    /// <summary>
    /// Re-syncs a leaf to its (already model-bound) <see cref="PredicateNodeForm.Field"/> after the
    /// user picks a new field: clears the now-meaningless value and snaps the operator to one the new
    /// field's type allows. The field itself is taken from <paramref name="nodes"/> — NOT passed in —
    /// so the user's dropdown choice is honoured rather than overwritten with a stale render-time value.
    /// </summary>
    public static void SetField(List<PredicateNodeForm> nodes, int id)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is null || node.Field is null) return;

        node.Value = "";
        node.Values = new List<string>();

        var allowed = LeafEditorOptions.OperatorTokensFor(node.Field).Select(o => o.Token).ToList();
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
