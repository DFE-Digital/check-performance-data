using System.Globalization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure folds between the <see cref="Predicate"/> object graph and the flat
/// <see cref="PredicateNodeForm"/> list the form binds. No I/O, no state.
/// </summary>
public static class PredicateForm
{
    public static List<PredicateNodeForm> Flatten(Predicate predicate)
    {
        var list = new List<PredicateNodeForm>();
        var counter = 0;

        void Walk(Predicate p, int? parentId)
        {
            var id = ++counter;
            list.Add(ToNode(p, id, parentId));
            switch (p)
            {
                case Predicate.AllOf a: foreach (var c in a.Items) Walk(c, id); break;
                case Predicate.AnyOf a: foreach (var c in a.Items) Walk(c, id); break;
                case Predicate.Not n:   Walk(n.Inner, id); break;
            }
        }

        Walk(predicate, null);
        return list;
    }

    /// <summary>
    /// Flattens for the editor, guaranteeing a COMPOSITE root so the editor always shows
    /// "Add condition"/"Add group". A bare leaf is wrapped in an AllOf first; existing
    /// AllOf/AnyOf/Not roots (and Otherwise) are flattened unchanged. RebuildPredicate
    /// collapses the wrapper again on save when it still has a single child.
    /// </summary>
    public static List<PredicateNodeForm> FlattenForEditing(Predicate predicate)
    {
        var rooted = predicate switch
        {
            Predicate.AllOf or Predicate.AnyOf or Predicate.Not or Predicate.Otherwise => predicate,
            _ => new Predicate.AllOf(new[] { predicate })
        };
        return Flatten(rooted);
    }

    public static Predicate RebuildPredicate(IReadOnlyList<PredicateNodeForm> nodes)
    {
        var childrenByParent = nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var root = nodes.FirstOrDefault(n => n.ParentId is null)
            ?? throw new ArgumentException("Predicate node list must contain exactly one root (ParentId == null).", nameof(nodes));
        var predicate = Build(root, childrenByParent);

        // Collapse a redundant single-child AllOf/AnyOf ROOT back to its child, so a branch
        // load-wrapped by FlattenForEditing round-trips to the identical bare leaf (no spurious
        // {"all":[...]} churn; describer/view-mode output unchanged).
        return predicate switch
        {
            Predicate.AllOf { Items.Count: 1 } a => a.Items[0],
            Predicate.AnyOf { Items.Count: 1 } a => a.Items[0],
            _ => predicate
        };
    }

    /// <summary>UI operator token for an existing predicate node (reverse of Normalize).</summary>
    public static string OperatorToken(PredicateKind kind, string? op) => kind switch
    {
        PredicateKind.FieldEq => "eq",
        PredicateKind.FieldNeq => "neq",
        PredicateKind.FieldIn => "in",
        PredicateKind.IsKnownAndCertain => "known",
        PredicateKind.OfficialLanguageIs => "lang",
        PredicateKind.FieldCompare => op?.ToLowerInvariant() ?? "lt",
        _ => string.Empty
    };

    private static Predicate Build(PredicateNodeForm node, Dictionary<int, List<PredicateNodeForm>> kids)
    {
        List<PredicateNodeForm> Children() =>
            kids.TryGetValue(node.Id, out var cs) ? cs : new List<PredicateNodeForm>();

        return node.Kind switch
        {
            PredicateKind.AllOf => new Predicate.AllOf(Children().Select(c => Build(c, kids)).ToList()),
            PredicateKind.AnyOf => new Predicate.AnyOf(Children().Select(c => Build(c, kids)).ToList()),
            PredicateKind.Not => new Predicate.Not(
                Children().Select(c => Build(c, kids)).FirstOrDefault() ?? Predicate.Otherwise.Instance),
            PredicateKind.FieldEq => new Predicate.FieldEq(node.Field ?? "", ParseValue(node.Field, node.Value)),
            PredicateKind.FieldNeq => new Predicate.FieldNeq(node.Field ?? "", ParseValue(node.Field, node.Value)),
            PredicateKind.FieldIn => new Predicate.FieldIn(node.Field ?? "",
                node.Values.Select(v => ParseValue(node.Field, v)).ToList()),
            // Fall back to Lt if Op is missing/unparseable; RuleSetValidator surfaces a real error on save.
            PredicateKind.FieldCompare => new Predicate.FieldCompare(node.Field ?? "",
                Enum.TryParse<CompareOp>(node.Op, out var cmp) ? cmp : CompareOp.Lt,
                ParseValue(node.Field, node.Value)),
            PredicateKind.IsKnownAndCertain => new Predicate.IsKnownAndCertain(node.Field ?? ""),
            PredicateKind.OfficialLanguageIs => new Predicate.OfficialLanguageIs(node.Field ?? "", node.Value ?? ""),
            _ => Predicate.Otherwise.Instance
        };
    }

    private static PredicateNodeForm ToNode(Predicate p, int id, int? parentId)
    {
        var node = new PredicateNodeForm { Id = id, ParentId = parentId };
        switch (p)
        {
            case Predicate.AllOf: node.Kind = PredicateKind.AllOf; break;
            case Predicate.AnyOf: node.Kind = PredicateKind.AnyOf; break;
            case Predicate.Not: node.Kind = PredicateKind.Not; break;
            case Predicate.FieldEq eq:
                node.Kind = PredicateKind.FieldEq; node.Field = eq.Field; node.Value = Scalar(eq.Value); break;
            case Predicate.FieldNeq neq:
                node.Kind = PredicateKind.FieldNeq; node.Field = neq.Field; node.Value = Scalar(neq.Value); break;
            case Predicate.FieldIn fin:
                node.Kind = PredicateKind.FieldIn; node.Field = fin.Field;
                node.Values = fin.Values.Select(Scalar).ToList(); break;
            case Predicate.FieldCompare cmp:
                node.Kind = PredicateKind.FieldCompare; node.Field = cmp.Field;
                node.Op = cmp.Op.ToString(); node.Value = Scalar(cmp.Value); break;
            case Predicate.IsKnownAndCertain k:
                node.Kind = PredicateKind.IsKnownAndCertain; node.Field = k.Field; break;
            case Predicate.OfficialLanguageIs l:
                node.Kind = PredicateKind.OfficialLanguageIs; node.Field = l.CountryField; node.Value = l.Language; break;
            case Predicate.Otherwise:
                node.Kind = PredicateKind.Otherwise; break;
        }
        node.Operator = OperatorToken(node.Kind, node.Op);
        return node;
    }

    private static string Scalar(FieldValue v) => v switch
    {
        FieldValue.Str s => s.Value,
        FieldValue.Bool b => b.Value ? "true" : "false",
        FieldValue.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldValue.Uncertain u => Scalar(u.Inner),
        _ => string.Empty
    };

    // Lenient: parse to the field's catalogue type; fall back to Str so RuleSetValidator
    // reports a friendly error on save rather than throwing mid-edit.
    private static FieldValue ParseValue(string? field, string? raw)
    {
        raw ??= string.Empty;
        if (field is not null && FieldCatalogue.TryGetType(field, out var type))
        {
            switch (type)
            {
                case FieldType.Bool when bool.TryParse(raw, out var b): return new FieldValue.Bool(b);
                case FieldType.Number when decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d):
                    return new FieldValue.Num(d);
                case FieldType.Date when DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt):
                    return new FieldValue.Date(dt);
                case FieldType.String: return new FieldValue.Str(raw);
            }
        }
        return new FieldValue.Str(raw);
    }
}
