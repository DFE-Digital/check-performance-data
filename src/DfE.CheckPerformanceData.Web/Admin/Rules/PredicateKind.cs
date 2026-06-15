namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Discriminator for a <see cref="PredicateNodeForm"/> in the flat editor list.</summary>
public enum PredicateKind
{
    AllOf, AnyOf, Not,
    FieldEq, FieldNeq, FieldIn, FieldCompare,
    IsKnownAndCertain, OfficialLanguageIs, Otherwise
}
