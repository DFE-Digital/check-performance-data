namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// A typed value carried in a <see cref="RuleContext"/> or referenced as a literal
/// inside a <see cref="Predicate"/>.
///
/// The tri-state shape (typed value | <see cref="Unknown"/> | <see cref="Uncertain"/>)
/// is what lets <see cref="Predicate.IsKnownAndCertain"/> have a meaning distinct
/// from a null check. The docx draws an explicit distinction between
/// "is currently known and certain" and "is unknown / uncertain", so the engine
/// preserves it end-to-end.
/// </summary>
public abstract record FieldValue
{
    public sealed record Str(string Value)    : FieldValue;
    public sealed record Bool(bool Value)     : FieldValue;
    public sealed record Num(decimal Value)   : FieldValue;
    public sealed record Date(DateOnly Value) : FieldValue;

    /// <summary>Field was not supplied at all (e.g. answer missing from the message).</summary>
    public sealed record Unknown : FieldValue
    {
        public static readonly Unknown Instance = new();
        private Unknown() { }
    }

    /// <summary>
    /// Field was supplied but the producer signalled low confidence (e.g. a
    /// future "How confident are you?" follow-up answered "not certain").
    /// Inner carries the value the producer believes to be correct so it can
    /// still be displayed in audit traces.
    /// </summary>
    public sealed record Uncertain(FieldValue Inner) : FieldValue;

    /// <summary>True when this is a concrete typed value (not Unknown, not Uncertain).</summary>
    public bool IsKnownAndCertain => this is Str or Bool or Num or Date;
}
