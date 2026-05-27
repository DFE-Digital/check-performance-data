namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// The expected runtime type of a canonical field. Drives:
/// (a) which <see cref="FieldValue"/> variant the mapper produces, and
/// (b) the load-time validation that a predicate's literal type matches the
///     field it references.
/// </summary>
public enum FieldType
{
    String,
    Bool,
    Number,
    Date
}
