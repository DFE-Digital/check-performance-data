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
            // Appends a line despite evaluating nothing. A terminal "otherwise" is the branch
            // an auditor most needs explained — it is how a request becomes Scrutiny without any
            // rule matching it — and a silent true would leave that decision with an empty trace
            // and so no detail row at all on the admin requests page (admin/windows/{id}/requests).
            case Predicate.Otherwise:
                Append(trace, "otherwise → true (no conditions to evaluate)");
                return TriResult.True;

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
        var windowStartDate = ctx.WindowStartDate;
        var literal = ResolveLiteral(value, windowStartDate);
        if (literal is null)
        {
            Append(trace, $"{field} == {Render(value, windowStartDate)} → unknown (relative date unresolvable)");
            return TriResult.Unknown;
        }
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} == {Render(literal, windowStartDate)} → unknown (got {Render(actual, windowStartDate)})");
            return TriResult.Unknown;
        }
        var result = Equal(actual, literal);
        Append(trace, $"{field} == {RenderLiteral(value, literal, windowStartDate)} → {Lower(result)} (got {Render(actual, windowStartDate)})");
        return result ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafNeq(string field, FieldValue value, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var windowStartDate = ctx.WindowStartDate;
        var literal = ResolveLiteral(value, windowStartDate);
        if (literal is null)
        {
            Append(trace, $"{field} != {Render(value, windowStartDate)} → unknown (relative date unresolvable)");
            return TriResult.Unknown;
        }
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} != {Render(literal, windowStartDate)} → unknown (got {Render(actual, windowStartDate)})");
            return TriResult.Unknown;
        }
        var result = !Equal(actual, literal);
        Append(trace, $"{field} != {RenderLiteral(value, literal, windowStartDate)} → {Lower(result)} (got {Render(actual, windowStartDate)})");
        return result ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafIn(string field, IReadOnlyList<FieldValue> values, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var windowStartDate = ctx.WindowStartDate;
        var resolved = new List<FieldValue>(values.Count);
        foreach (var v in values)
        {
            var r = ResolveLiteral(v, windowStartDate);
            if (r is null)
            {
                var pending = string.Join(",", values.Select(x => Render(x, windowStartDate)));
                Append(trace, $"{field} in [{pending}] → unknown (relative date unresolvable)");
                return TriResult.Unknown;
            }
            resolved.Add(r);
        }
        var literals = string.Join(
            ",",
            resolved.Zip(values).Select(p => RenderLiteral(p.Second, p.First, windowStartDate)));
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} in [{literals}] → unknown (got {Render(actual, windowStartDate)})");
            return TriResult.Unknown;
        }
        var hit = resolved.Any(v => Equal(actual, v));
        Append(trace, $"{field} in [{literals}] → {Lower(hit)} (got {Render(actual, windowStartDate)})");
        return hit ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafCompare(string field, CompareOp op, FieldValue value, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var opSym = SymbolOf(op);
        var windowStartDate = ctx.WindowStartDate;
        var literal = ResolveLiteral(value, windowStartDate);
        if (literal is null)
        {
            Append(trace, $"{field} {opSym} {Render(value, windowStartDate)} → unknown (relative date unresolvable)");
            return TriResult.Unknown;
        }
        if (!actual.IsKnownAndCertain)
        {
            Append(trace, $"{field} {opSym} {Render(literal, windowStartDate)} → unknown (got {Render(actual, windowStartDate)})");
            return TriResult.Unknown;
        }

        int cmp;
        switch (actual)
        {
            case FieldValue.Num an when literal is FieldValue.Num bn:
                cmp = decimal.Compare(an.Value, bn.Value);
                break;
            case FieldValue.Date ad when literal is FieldValue.Date bd:
                cmp = ad.Value.CompareTo(bd.Value);
                break;
            default:
                Append(trace, $"{field} {opSym} {Render(literal, windowStartDate)} → unknown (type mismatch: got {Render(actual, windowStartDate)})");
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
        Append(trace, $"{field} {opSym} {RenderLiteral(value, literal, windowStartDate)} → {Lower(pass)} (got {Render(actual, windowStartDate)})");
        return pass ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafIsKnown(string field, RuleContext ctx, List<string> trace)
    {
        var actual = ctx.GetField(field);
        var known = actual.IsKnownAndCertain;
        Append(trace, $"isKnownAndCertain({field}) → {Lower(known)} (got {Render(actual, ctx.WindowStartDate)})");
        return known ? TriResult.True : TriResult.False;
    }

    private static TriResult LeafOfficialLanguage(Predicate.OfficialLanguageIs lang, RuleContext ctx, Lookups lookups, List<string> trace)
    {
        var actual = ctx.GetField(lang.CountryField);
        if (actual is not FieldValue.Str s || !actual.IsKnownAndCertain)
        {
            Append(trace, $"officialLanguageIs('{lang.Language}', {lang.CountryField}) → unknown (got {Render(actual, ctx.WindowStartDate)})");
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

    /// <summary>
    /// A rule literal in trace text, next to the value the engine actually
    /// compared. A relative date that resolved shows the resolved date plus
    /// the window-relative form name, so an auditor can tell the cutoff came
    /// from the window, not a fixed date (#800). Any other literal renders
    /// plain.
    /// </summary>
    private static string RenderLiteral(FieldValue original, FieldValue? resolved, DateOnly? windowStartDate)
    {
        // A resolved relative literal is always a concrete date here; a null would mean the
        // form did not resolve, and those leaves trace through the unresolvable path instead.
        if (resolved is not { } value) return "unknown";
        if (original is FieldValue.RelativeDate rd)
            return $"{Render(value, windowStartDate)} (window-relative: {rd.Spec.WindowRelative})";
        return Render(value, windowStartDate);
    }

    /// <summary>
    /// A field value or rule literal in trace text. A relative date that
    /// cannot resolve in this window shows the form name only; a resolved
    /// literal never reaches this method (the leaf renders it through
    /// <see cref="RenderLiteral"/> with the form name) (#800).
    /// </summary>
    private static string Render(FieldValue v, DateOnly? windowStartDate) => v switch
    {
        FieldValue.Str s        => $"\"{s.Value}\"",
        FieldValue.Bool b       => b.Value ? "true" : "false",
        FieldValue.Num n        => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d       => d.Value.ToString("yyyy-MM-dd"),
        FieldValue.Unknown      => "unknown",
        FieldValue.RelativeDate rd
            when RelativeDateResolver.TryResolve(rd.Spec, windowStartDate, out var date)
            => $"{date:yyyy-MM-dd} (window-relative: {rd.Spec.WindowRelative})",
        FieldValue.RelativeDate rd => $"window-relative: {rd.Spec.WindowRelative}",
        FieldValue.Uncertain u  => $"uncertain({Render(u.Inner, windowStartDate)})",
        _ => "?",
    };

    /// <summary>
    /// Resolves a rule literal before a leaf compares it (#800). A relative
    /// date resolves against the checking window start date; null means the
    /// form has no date in this window and the leaf must not decide. Any
    /// other literal passes through unchanged.
    /// </summary>
    private static FieldValue? ResolveLiteral(FieldValue value, DateOnly? windowStartDate) => value switch
    {
        FieldValue.RelativeDate rd
            when RelativeDateResolver.TryResolve(rd.Spec, windowStartDate, out var date)
            => new FieldValue.Date(date),
        // A relative date with no date in this window resolves to no value, so
        // the leaf returns Unknown and the branch cannot match (fail closed).
        FieldValue.RelativeDate => null,
        _ => value,
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
