namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Maps a leaf node's UI <see cref="PredicateNodeForm.Operator"/> token onto its
/// authoritative <see cref="PredicateNodeForm.Kind"/> and <see cref="PredicateNodeForm.Op"/>.
/// Applied to every leaf on each postback before transforms / save / re-render.
/// Composite and Otherwise nodes are left untouched.
/// </summary>
public static class LeafNormalizer
{
    public static void Normalize(PredicateNodeForm node)
    {
        if (node.Kind is PredicateKind.AllOf or PredicateKind.AnyOf or PredicateKind.Not or PredicateKind.Otherwise
            && string.IsNullOrEmpty(node.Operator))
        {
            return; // structural node, no operator
        }

        switch (node.Operator)
        {
            case "eq": node.Kind = PredicateKind.FieldEq; node.Op = null; break;
            case "neq": node.Kind = PredicateKind.FieldNeq; node.Op = null; break;
            case "in": node.Kind = PredicateKind.FieldIn; node.Op = null; break;
            case "known": node.Kind = PredicateKind.IsKnownAndCertain; node.Op = null; break;
            case "lang": node.Kind = PredicateKind.OfficialLanguageIs; node.Op = null; break;
            case "lt": node.Kind = PredicateKind.FieldCompare; node.Op = "Lt"; break;
            case "lte": node.Kind = PredicateKind.FieldCompare; node.Op = "Lte"; break;
            case "gt": node.Kind = PredicateKind.FieldCompare; node.Op = "Gt"; break;
            case "gte": node.Kind = PredicateKind.FieldCompare; node.Op = "Gte"; break;
        }
    }

    public static void NormalizeAll(IEnumerable<PredicateNodeForm> nodes)
    {
        foreach (var n in nodes) Normalize(n);
    }
}
