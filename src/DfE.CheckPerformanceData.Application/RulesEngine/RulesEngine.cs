using System.Globalization;

namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Pure evaluator. Walks a validated <see cref="RuleSet"/> against a
/// <see cref="RuleContext"/> and emits a <see cref="Decision"/> with an audit
/// trace. Never throws on well-formed (validated) input.
/// </summary>
public sealed class RulesEngine : IRulesEngine
{
    private const int MaxTraceLines = 50;

    public Decision Evaluate(RuleSet rules, RuleContext ctx, Lookups lookups)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(lookups);

        var outcome = rules.Outcomes.FirstOrDefault(o => string.Equals(o.Key, ctx.OutcomeKey, StringComparison.Ordinal));
        if (outcome is null)
        {
            return Decision.UnmatchedOutcome(ctx.OutcomeKey);
        }

        foreach (var branch in outcome.Rules)
        {
            var trace = new List<string>();
            var result = Eval(branch.When, ctx, lookups, trace);
            if (result == TriResult.True)
            {
                return new Decision(branch.Status, outcome.Key, branch.Id, trace);
            }
            // result is False or Unknown -> branch does not match; try the next.
        }

        // The validator guarantees a terminal "otherwise" exists, so reaching here means
        // the ruleset bypassed validation. Default to Scrutiny per the safety policy.
        return Decision.NoMatch(outcome.Key, Array.Empty<string>());
    }

    // --- three-valued evaluation ---

    private enum TriResult { True, False, Unknown }

    private static TriResult Eval(Predicate predicate, RuleContext ctx, Lookups lookups, List<string> trace)
    {
        switch (predicate)
        {
            case Predicate.Otherwise: return TriResult.True;

            case Predicate.AllOf all:
            {
                var anyUnknown = false;
                foreach (var item in all.Items)
                {
                    var r = Eval(item, ctx, lookups, trace);
                    if (r == TriResult.False) return TriResult.False;
                    if (r == TriResult.Unknown) anyUnknown = true;
                }
                return anyUnknown ? TriResult.Unknown : TriResult.True;
            }

            case Predicate.AnyOf any:
            {
                var anyUnknown = false;
                foreach (var item in any.Items)
                {
                    var r = Eval(item, ctx, lookups, trace);
                    if (r == TriResult.True) return TriResult.True;
                    if (r == TriResult.Unknown) anyUnknown = true;
                }
                return anyUnknown ? TriResult.Unknown : TriResult.False;
            }

            case Predicate.Not not:
            {
                var r = Eval(not.Inner, ctx, lookups, trace);
                return r switch
                {
                    TriResult.True => TriResult.False,
                    TriResult.False => TriResult.True,
                    _ => TriResult.Unknown,
                };
            }

            case Predicate.FieldEq eq:       return LeafEq(eq.Field, eq.Value, ctx, trace);
            case Predicate.FieldNeq neq:     return LeafNeq(neq.Field, neq.Value, ctx, trace);
            case Predicate.FieldIn inP:      return LeafIn(inP.Field, inP.Values, ctx, trace);
            case Predicate.FieldCompare cmp: return LeafCompare(cmp.Field, cmp.Op, cmp.Value, ctx, trace);
            case Predicate.IsKnownAndCertain iknown: return LeafIsKnown(iknown.Field, ctx, trace);
            case Predicate.OfficialLanguageIs lang: return LeafOfficialLanguage(lang, ctx, lookups, trace);

            default: throw new InvalidOperationException($"Unhandled predicate {predicate.GetType().Name}.");
        }
    }

    // --- leaf evaluations (each appends one trace line) ---

    private static TriResult LeafEq(string field, FieldValue value, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} == {Render(value)} → unknown (got {Render(actual)})");
            return TriResult.Unknown;
        }
        var result = Equal(actual, value);
        Append(trace, $"{field} == {Render(value)} → {Lower(result)} (got {Render(actual)})");
        return result ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafNeq(string field, FieldValue value, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} != {Render(value)} → unknown (got {Render(actual)})");
            return TriResult.Unknown;
        }
        var result = !Equal(actual, value);
        Append(trace, $"{field} != {Render(value)} → {Lower(result)} (got {Render(actual)})");
        return result ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafIn(string field, IReadOnlyList<FieldValue> values, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var literals = string.Join(",", values.Select(Render));
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} in [{literals}] → unknown (got {Render(actual)})");
            return TriResult.Unknown;
        }
        var hit = values.Any(v => Equal(actual, v));
        Append(trace, $"{field} in [{literals}] → {Lower(hit)} (got {Render(actual)})");
        return hit ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafCompare(string field, CompareOp op, FieldValue value, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var opSym = SymbolOf(op);
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} {opSym} {Render(value)} → unknown (got {Render(actual)})");
            return TriResult.Unknown;
        }

        int cmp;
        switch (actual)
        {
            case FieldValue.Num an when value is FieldValue.Num bn:
                cmp = decimal.Compare(an.Value, bn.Value);
                break;
            case FieldValue.Date ad when value is FieldValue.Date bd:
                cmp = ad.Value.CompareTo(bd.Value);
                break;
            default:
                Append(trace, $"{field} {opSym} {Render(value)} → unknown (type mismatch: got {Render(actual)})");
                return TriResult.Unknown;
        }

        var pass = op switch
        {
            CompareOp.Lt  => cmp < 0,
            CompareOp.Lte => cmp <= 0,
            CompareOp.Gt  => cmp > 0,
            CompareOp.Gte => cmp >= 0,
            _ => false,
        };
        Append(trace, $"{field} {opSym} {Render(value)} → {Lower(pass)} (got {Render(actual)})");
        return pass ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafIsKnown(string field, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var known = actual.IsKnownAndCertain;
        Append(trace, $"isKnownAndCertain({field}) → {Lower(known)} (got {Render(actual)})");
        return known ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafOfficialLanguage(Predicate.OfficialLanguageIs lang, RuleContext ctx, Lookups lookups, List<string> trace)
    {
        var actual = ctx.GetField(lang.CountryField);
        if (actual is not FieldValue.Str s || !actual.IsKnownAndCertain)
        {
            Append(trace, $"officialLanguageIs('{lang.Language}', {lang.CountryField}) → unknown (got {Render(actual)})");
            return TriResult.Unknown;
        }
        var result = lookups.CountryHasOfficialLanguage(s.Value, lang.Language);
        Append(trace, $"officialLanguageIs('{lang.Language}', {lang.CountryField}={s.Value}) → {Lower(result)}");
        return result ? TriResult.True : TriResult.False;
    }

    // --- helpers ---

    private static bool Equal(FieldValue a, FieldValue b) => (a, b) switch
    {
        (FieldValue.Str sa,  FieldValue.Str sb)  => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
        (FieldValue.Bool ba, FieldValue.Bool bb) => ba.Value == bb.Value,
        (FieldValue.Num na,  FieldValue.Num nb)  => na.Value == nb.Value,
        (FieldValue.Date da, FieldValue.Date db) => da.Value == db.Value,
        _ => false,
    };

    private static string Render(FieldValue v) => v switch
    {
        FieldValue.Str s        => $"\"{s.Value}\"",
        FieldValue.Bool b       => b.Value ? "true" : "false",
        FieldValue.Num n        => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d       => d.Value.ToString("yyyy-MM-dd"),
        FieldValue.Unknown      => "unknown",
        FieldValue.Uncertain u  => $"uncertain({Render(u.Inner)})",
        _ => "?",
    };

    private static string Lower(bool b) => b ? "true" : "false";

    private static string SymbolOf(CompareOp op) => op switch
    {
        CompareOp.Lt => "<",
        CompareOp.Lte => "<=",
        CompareOp.Gt => ">",
        CompareOp.Gte => ">=",
        _ => "?",
    };

    private static void Append(List<string> trace, string line)
    {
        if (trace.Count >= MaxTraceLines)
        {
            if (trace.Count == MaxTraceLines)
            {
                trace.Add($"... trace truncated at {MaxTraceLines} lines.");
            }
            return;
        }
        trace.Add(line);
    }
}
