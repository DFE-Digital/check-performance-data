using System.Globalization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure rendering of a <see cref="Predicate"/> tree into a human-readable
/// <see cref="PredicateNode"/> tree for the read-only admin views. No I/O, no state.
/// </summary>
public static class PredicateDescriber
{
    public static PredicateNode Describe(Predicate predicate) => predicate switch
    {
        Predicate.AllOf all => new PredicateNode(
            "All of the following are true:",
            all.Items.Select(Describe).ToList()),

        Predicate.AnyOf any => new PredicateNode(
            "Any of the following are true:",
            any.Items.Select(Describe).ToList()),

        Predicate.Not not => new PredicateNode(
            "Not true:",
            new[] { Describe(not.Inner) }),

        Predicate.FieldEq eq => PredicateNode.Leaf($"{eq.Field} is {Value(eq.Value)}"),
        Predicate.FieldNeq neq => PredicateNode.Leaf($"{neq.Field} is not {Value(neq.Value)}"),
        Predicate.FieldIn fin => PredicateNode.Leaf(
            $"{fin.Field} is one of: {string.Join(", ", fin.Values.Select(Value))}"),
        Predicate.FieldCompare cmp => PredicateNode.Leaf(
            $"{cmp.Field} {Op(cmp.Op)} {Value(cmp.Value)}"),
        Predicate.IsKnownAndCertain known => PredicateNode.Leaf(
            $"{known.Field} is known and certain"),
        Predicate.OfficialLanguageIs lang => PredicateNode.Leaf(
            $"\"{lang.Language}\" is an official language of the country in {lang.CountryField}"),
        Predicate.Otherwise => PredicateNode.Leaf("Otherwise (always matches)"),

        _ => PredicateNode.Leaf("Unknown condition")
    };

    private static string Op(CompareOp op) => op switch
    {
        CompareOp.Lt => "is less than",
        CompareOp.Lte => "is less than or equal to",
        CompareOp.Gt => "is greater than",
        CompareOp.Gte => "is greater than or equal to",
        _ => op.ToString()
    };

    private static string Value(FieldValue value) => value switch
    {
        FieldValue.Str s => $"\"{s.Value}\"",
        FieldValue.Bool b => b.Value ? "true" : "false",
        FieldValue.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldValue.Uncertain u => $"{Value(u.Inner)} (uncertain)",
        FieldValue.Unknown => "unknown",
        _ => "unknown"
    };
}
