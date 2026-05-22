namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>Numeric / date comparison operator used by <see cref="Predicate.FieldCompare"/>.</summary>
public enum CompareOp
{
    Lt,
    Lte,
    Gt,
    Gte
}
