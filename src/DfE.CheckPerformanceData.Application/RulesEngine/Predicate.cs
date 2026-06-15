namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Discriminated-union of every shape allowed inside a rule's <c>when</c> clause.
/// Deserialized from JSON by <c>PredicateJsonConverter</c>; the shape on disk
/// mirrors the docx vocabulary one-for-one (all / any / not / field+op / otherwise).
/// </summary>
public abstract record Predicate
{
    /// <summary>All children must match. Short-circuits on first false.</summary>
    public sealed record AllOf(IReadOnlyList<Predicate> Items) : Predicate;

    /// <summary>Any child matches. Short-circuits on first true.</summary>
    public sealed record AnyOf(IReadOnlyList<Predicate> Items) : Predicate;

    /// <summary>
    /// Unary negation. Uncertainty is preserved (Unknown stays Unknown);
    /// at the branch boundary Unknown is projected to false per the
    /// "uncertainty never auto-decides" rule.
    /// </summary>
    public sealed record Not(Predicate Inner) : Predicate;

    /// <summary>Field equals literal value.</summary>
    public sealed record FieldEq(string Field, FieldValue Value) : Predicate;

    /// <summary>Field does NOT equal literal value (false if field is Unknown).</summary>
    public sealed record FieldNeq(string Field, FieldValue Value) : Predicate;

    /// <summary>Field equals any of the listed values. Used for code lists (e.g. inclusion flag).</summary>
    public sealed record FieldIn(string Field, IReadOnlyList<FieldValue> Values) : Predicate;

    /// <summary>Numeric or date comparison (lt/lte/gt/gte).</summary>
    public sealed record FieldCompare(string Field, CompareOp Op, FieldValue Value) : Predicate;

    /// <summary>True iff field is a concrete typed value (not Unknown, not Uncertain).</summary>
    public sealed record IsKnownAndCertain(string Field) : Predicate;

    /// <summary>
    /// True iff the looked-up country (via <paramref name="CountryField"/>) has
    /// the named language as one of its official languages, per the
    /// country-languages lookup blob.
    /// </summary>
    public sealed record OfficialLanguageIs(string CountryField, string Language) : Predicate;

    /// <summary>Terminal catch-all. Always true. Must be the last branch in every outcome.</summary>
    public sealed record Otherwise : Predicate
    {
        public static readonly Otherwise Instance = new();
        private Otherwise() { }
    }
}
