namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// A renderable node in a described predicate tree. Leaves carry a single phrase
/// (e.g. "checkingWindowType is \"KS4June\""); composites carry a header (e.g. "All of the
/// following are true:") plus child nodes. The recursive Razor partial renders
/// composites as a nested list for accessibility.
/// </summary>
public sealed record PredicateNode(string Text, IReadOnlyList<PredicateNode> Children)
{
    public bool IsLeaf => Children.Count == 0;

    public static PredicateNode Leaf(string text) => new(text, Array.Empty<PredicateNode>());
}
