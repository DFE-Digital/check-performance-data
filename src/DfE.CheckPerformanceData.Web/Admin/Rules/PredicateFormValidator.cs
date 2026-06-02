namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Web-layer structural pre-check run before <c>SaveRulesAsync</c>. Stops half-built
/// trees (notably an empty AllOf/AnyOf, which is vacuously true and would match every
/// request) from reaching the service. Field-existence and literal-type errors are left
/// to the Application <c>RuleSetValidator</c> on save.
/// </summary>
public static class PredicateFormValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<PredicateNodeForm> nodes)
    {
        var errors = new List<string>();
        if (nodes.Count == 0)
        {
            errors.Add("The branch must have at least one condition.");
            return errors;
        }
        foreach (var node in nodes)
        {
            var childCount = nodes.Count(n => n.ParentId == node.Id);
            switch (node.Kind)
            {
                case PredicateKind.AllOf:
                case PredicateKind.AnyOf:
                    if (childCount == 0)
                        errors.Add("A group must contain at least one condition.");
                    break;
                case PredicateKind.Not:
                    if (childCount != 1)
                        errors.Add("A 'not' group must contain exactly one condition.");
                    break;
                case PredicateKind.FieldIn:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (node.Values.Count == 0 || node.Values.All(string.IsNullOrWhiteSpace))
                        errors.Add($"'{node.Field}' must list at least one value.");
                    break;
                case PredicateKind.IsKnownAndCertain:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    break;
                case PredicateKind.OfficialLanguageIs:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (string.IsNullOrWhiteSpace(node.Value))
                        errors.Add("An 'official language is' condition needs a language.");
                    break;
                case PredicateKind.FieldEq:
                case PredicateKind.FieldNeq:
                case PredicateKind.FieldCompare:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (string.IsNullOrWhiteSpace(node.Value))
                        errors.Add($"'{node.Field}' needs a value.");
                    break;
            }
        }
        return errors;
    }
}
