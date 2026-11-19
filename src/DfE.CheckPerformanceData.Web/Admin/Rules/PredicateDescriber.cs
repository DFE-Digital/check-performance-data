using System.Globalization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure rendering of a <see cref="Predicate"/> tree into a human-readable
/// <see cref="PredicateNode"/> tree for the read-only admin views. No I/O, no state.
/// </summary>
public static class PredicateDescriber
{
    public static PredicateNode Describe(Predicate predicate) => Describe(predicate, windowStartDate: null);

    /// <summary>
    /// Describes with the window's start date, so a window-relative date literal resolves to
    /// the concrete date for that window (#800). Without it, a relative literal shows its
    /// form name and no date — the describer never guesses.
    /// </summary>
    public static PredicateNode Describe(Predicate predicate, DateOnly? windowStartDate) => predicate switch
    {
        Predicate.AllOf all => new PredicateNode(
            "All of the following are true:",
            all.Items.Select(p => Describe(p, windowStartDate)).ToList()),

        Predicate.AnyOf any => new PredicateNode(
            "Any of the following are true:",
            any.Items.Select(p => Describe(p, windowStartDate)).ToList()),

        Predicate.Not not => new PredicateNode(
            "Not true:",
            new[] { Describe(not.Inner, windowStartDate) }),

        Predicate.FieldEq eq => PredicateNode.Leaf($"{eq.Field} is {Value(eq.Value, windowStartDate)}"),
        Predicate.FieldNeq neq => PredicateNode.Leaf($"{neq.Field} is not {Value(neq.Value, windowStartDate)}"),
        Predicate.FieldIn fin => PredicateNode.Leaf(
            $"{fin.Field} is one of: {string.Join(", ", fin.Values.Select(v => Value(v, windowStartDate)))}"),
        Predicate.FieldCompare cmp => PredicateNode.Leaf(
            $"{cmp.Field} {Op(cmp.Op)} {Value(cmp.Value, windowStartDate)}"),
        Predicate.IsKnownAndCertain known => PredicateNode.Leaf(
            $"{known.Field} is known and certain"),
        Predicate.OfficialLanguageIs lang => PredicateNode.Leaf(
            $"\"{lang.Language}\" is an official language of the country in {lang.CountryField}"),
        Predicate.Otherwise => PredicateNode.Leaf("Otherwise (always matches)"),

        _ => PredicateNode.Leaf("Unknown condition")
    };

    /// <summary>The plain name of a window-relative date form, shown where no date resolves.</summary>
    public static string FormName(RelativeDateSpec spec) => spec switch
    {
        RelativeDateSpec.SecondMondayOfMay => "second Monday of May",
        RelativeDateSpec.FirstSeptemberMinusYears y => $"1 September, {y.Years} years before the window year",
        RelativeDateSpec.DayMonthOfWindowYear d => $"{d.Day} {MonthName(d.Month)} of the window year",
        _ => spec.WindowRelative
    };

    private static string MonthName(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static string Op(CompareOp op) => op switch
    {
        CompareOp.Lt => "is less than",
        CompareOp.Lte => "is less than or equal to",
        CompareOp.Gt => "is greater than",
        CompareOp.Gte => "is greater than or equal to",
        _ => op.ToString()
    };

    private static string Value(FieldValue value, DateOnly? windowStartDate) => value switch
    {
        FieldValue.Str s => $"\"{s.Value}\"",
        FieldValue.Bool b => b.Value ? "true" : "false",
        FieldValue.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldValue.RelativeDate rd => RelativeDateText(rd.Spec, windowStartDate),
        FieldValue.Uncertain u => $"{Value(u.Inner, windowStartDate)} (uncertain)",
        FieldValue.Unknown => "unknown",
        _ => "unknown"
    };

    /// <summary>
    /// A resolvable relative literal shows the resolved date first, then the form name
    /// (for example "10 May 2027 (window-relative: second Monday of May)"). A relative
    /// literal that cannot resolve shows the form name with no date.
    /// </summary>
    private static string RelativeDateText(RelativeDateSpec spec, DateOnly? windowStartDate)
    {
        var name = FormName(spec);
        if (RelativeDateResolver.TryResolve(spec, windowStartDate, out var date))
        {
            return $"{date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} (window-relative: {name})";
        }
        return name;
    }
}
