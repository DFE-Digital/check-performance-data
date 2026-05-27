using System.Globalization;

namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Result of validating a parsed <see cref="RuleSet"/> against the field catalogue
/// and structural rules. On success carries a <see cref="ResolvedRules"/> whose
/// predicate literals have been coerced to match the catalogue's declared types
/// (e.g. ISO date strings → <see cref="FieldValue.Date"/>).
/// </summary>
public sealed record RuleSetValidationResult(
    bool IsValid,
    RuleSet? ResolvedRules,
    IReadOnlyList<string> Errors)
{
    public static RuleSetValidationResult Success(RuleSet resolved) =>
        new(true, resolved, Array.Empty<string>());

    public static RuleSetValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>
/// Validates a freshly-parsed <see cref="RuleSet"/> and produces a resolved copy
/// where every literal has the correct <see cref="FieldValue"/> shape for its
/// field. Run once on load, before the new ruleset is allowed to replace the
/// current snapshot — a validation failure means the previous good copy stays in
/// place (see <c>BlobRulesProvider</c>).
///
/// Validation rules enforced (in order, all errors collected before returning):
/// <list type="number">
///   <item>Outcomes list non-empty.</item>
///   <item>Every outcome has a non-empty <c>Key</c> and at least one branch.</item>
///   <item>Every outcome's final branch is <see cref="Predicate.Otherwise"/>.</item>
///   <item>No earlier branch within an outcome is <see cref="Predicate.Otherwise"/>.</item>
///   <item>Every <c>field</c> referenced exists in <see cref="FieldCatalogue"/>.</item>
///   <item>Every literal coerces successfully to the field's catalogue type.</item>
///   <item>Branch IDs are unique within an outcome.</item>
/// </list>
/// </summary>
public sealed class RuleSetValidator
{
    public RuleSetValidationResult Validate(RuleSet rules)
    {
        var errors = new List<string>();

        if (rules is null)
        {
            return RuleSetValidationResult.Failure(new[] { "RuleSet was null." });
        }

        if (rules.Outcomes.Count == 0)
        {
            errors.Add("RuleSet.Outcomes is empty.");
        }

        var resolvedOutcomes = new List<OutcomeRules>(rules.Outcomes.Count);
        foreach (var outcome in rules.Outcomes)
        {
            resolvedOutcomes.Add(ValidateOutcome(outcome, errors));
        }

        return errors.Count == 0
            ? RuleSetValidationResult.Success(rules with { Outcomes = resolvedOutcomes })
            : RuleSetValidationResult.Failure(errors);
    }

    // --- outcome-level ---

    private OutcomeRules ValidateOutcome(OutcomeRules outcome, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(outcome.Key))
        {
            errors.Add("Outcome has empty key.");
        }

        if (outcome.Rules.Count == 0)
        {
            errors.Add($"Outcome '{outcome.Key}' has no rules.");
            return outcome;
        }

        // Terminal must be Otherwise; no earlier branch may be Otherwise.
        for (var i = 0; i < outcome.Rules.Count; i++)
        {
            var isLast = i == outcome.Rules.Count - 1;
            var branch = outcome.Rules[i];
            if (!isLast && branch.When is Predicate.Otherwise)
            {
                errors.Add($"Outcome '{outcome.Key}' branch '{branch.Id}' uses 'otherwise' before the last position.");
            }
            if (isLast && branch.When is not Predicate.Otherwise)
            {
                errors.Add($"Outcome '{outcome.Key}' final branch '{branch.Id}' must be 'otherwise'.");
            }
        }

        // Unique branch IDs within an outcome.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in outcome.Rules)
        {
            if (!ids.Add(branch.Id))
            {
                errors.Add($"Outcome '{outcome.Key}' has duplicate branch id '{branch.Id}'.");
            }
        }

        // Resolve each branch's predicate (coerces literals where the field type demands it).
        var resolvedBranches = new List<RuleBranch>(outcome.Rules.Count);
        foreach (var branch in outcome.Rules)
        {
            var resolved = ResolvePredicate(branch.When, outcome.Key, branch.Id, errors);
            resolvedBranches.Add(branch with { When = resolved });
        }

        return outcome with { Rules = resolvedBranches };
    }

    // --- predicate-level ---

    private Predicate ResolvePredicate(Predicate predicate, string outcomeKey, string branchId, List<string> errors)
    {
        switch (predicate)
        {
            case Predicate.AllOf all:
                return new Predicate.AllOf(
                    all.Items.Select(p => ResolvePredicate(p, outcomeKey, branchId, errors)).ToList());

            case Predicate.AnyOf any:
                return new Predicate.AnyOf(
                    any.Items.Select(p => ResolvePredicate(p, outcomeKey, branchId, errors)).ToList());

            case Predicate.Not not:
                return new Predicate.Not(ResolvePredicate(not.Inner, outcomeKey, branchId, errors));

            case Predicate.FieldEq eq:
                return new Predicate.FieldEq(eq.Field,
                    CoerceLiteral(eq.Field, eq.Value, outcomeKey, branchId, errors));

            case Predicate.FieldNeq neq:
                return new Predicate.FieldNeq(neq.Field,
                    CoerceLiteral(neq.Field, neq.Value, outcomeKey, branchId, errors));

            case Predicate.FieldIn inP:
                if (inP.Values.Count == 0)
                {
                    errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' has empty 'in' list for field '{inP.Field}'.");
                }
                return new Predicate.FieldIn(inP.Field,
                    inP.Values.Select(v => CoerceLiteral(inP.Field, v, outcomeKey, branchId, errors)).ToList());

            case Predicate.FieldCompare cmp:
                ExpectComparableField(cmp.Field, outcomeKey, branchId, errors);
                return new Predicate.FieldCompare(cmp.Field, cmp.Op,
                    CoerceLiteral(cmp.Field, cmp.Value, outcomeKey, branchId, errors));

            case Predicate.IsKnownAndCertain iknown:
                ExpectKnownField(iknown.Field, outcomeKey, branchId, errors);
                return iknown;

            case Predicate.OfficialLanguageIs lang:
                ExpectStringField(lang.CountryField, outcomeKey, branchId, errors);
                if (string.IsNullOrWhiteSpace(lang.Language))
                {
                    errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' officialLanguageIs requires a non-empty language.");
                }
                return lang;

            case Predicate.Otherwise:
                return predicate;

            default:
                errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' has unsupported predicate {predicate.GetType().Name}.");
                return predicate;
        }
    }

    // --- field + literal validation ---

    private static void ExpectKnownField(string field, string outcomeKey, string branchId, List<string> errors)
    {
        if (!FieldCatalogue.Contains(field))
        {
            errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' references unknown field '{field}'.");
        }
    }

    private static void ExpectStringField(string field, string outcomeKey, string branchId, List<string> errors)
    {
        ExpectKnownField(field, outcomeKey, branchId, errors);
        if (FieldCatalogue.TryGetType(field, out var t) && t != FieldType.String)
        {
            errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' expects '{field}' to be String but catalogue declares {t}.");
        }
    }

    private static void ExpectComparableField(string field, string outcomeKey, string branchId, List<string> errors)
    {
        ExpectKnownField(field, outcomeKey, branchId, errors);
        if (FieldCatalogue.TryGetType(field, out var t) && t is not (FieldType.Number or FieldType.Date))
        {
            errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' cannot use lt/lte/gt/gte on '{field}' (type {t}).");
        }
    }

    /// <summary>
    /// Coerce a literal into the shape the catalogue declares for its field.
    /// Friendly coercions (Num → Str where field is Str; Str → Date where ISO date)
    /// keep the JSON readable for business users without sacrificing type safety.
    /// </summary>
    private static FieldValue CoerceLiteral(string field, FieldValue value, string outcomeKey, string branchId,
        List<string> errors)
    {
        if (!FieldCatalogue.TryGetType(field, out var type))
        {
            errors.Add($"Outcome '{outcomeKey}' branch '{branchId}' references unknown field '{field}'.");
            return value;
        }

        return (type, value) switch
        {
            (FieldType.String, FieldValue.Str s) => s,
            (FieldType.String, FieldValue.Num n) => new FieldValue.Str(n.Value.ToString(CultureInfo.InvariantCulture)),
            (FieldType.String, FieldValue.Bool b) => Fail<FieldValue.Str>(
                $"Outcome '{outcomeKey}' branch '{branchId}' literal for String field '{field}' is bool, not string.",
                errors, () => new FieldValue.Str(b.Value ? "true" : "false")),

            (FieldType.Bool, FieldValue.Bool b)  => b,
            (FieldType.Bool, var other)          => Fail<FieldValue.Bool>(
                $"Outcome '{outcomeKey}' branch '{branchId}' literal for Bool field '{field}' is {other.GetType().Name}.",
                errors, () => new FieldValue.Bool(false)),

            (FieldType.Number, FieldValue.Num n) => n,
            (FieldType.Number, FieldValue.Str s) when decimal.TryParse(s.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                => new FieldValue.Num(d),
            (FieldType.Number, var other)        => Fail<FieldValue.Num>(
                $"Outcome '{outcomeKey}' branch '{branchId}' literal for Number field '{field}' is {other.GetType().Name}.",
                errors, () => new FieldValue.Num(0)),

            (FieldType.Date, FieldValue.Str s) when DateOnly.TryParseExact(s.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                => new FieldValue.Date(d),
            (FieldType.Date, FieldValue.Date d)  => d,
            (FieldType.Date, var other)          => Fail<FieldValue.Date>(
                $"Outcome '{outcomeKey}' branch '{branchId}' literal for Date field '{field}' is not an ISO yyyy-MM-dd string (got {other.GetType().Name}).",
                errors, () => new FieldValue.Date(DateOnly.MinValue)),

            _ => value
        };
    }

    private static T Fail<T>(string message, List<string> errors, Func<T> fallback) where T : FieldValue
    {
        errors.Add(message);
        return fallback();
    }
}
