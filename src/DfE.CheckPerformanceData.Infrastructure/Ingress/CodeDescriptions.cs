using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>Every code description a schema asks ingress to write.</summary>
internal sealed class CodeDescriptions
{
    private const string IngressExtension = "x-ingress";
    private const string RulesKey = "codeDescriptions";
    private const string RulesPath = IngressExtension + "." + RulesKey;

    private readonly IReadOnlyList<CodeDescriptionRule> _schemaRules;
    private readonly IReadOnlyList<CodeDescriptionRule> _allRules;
    private readonly bool _isList;

    private CodeDescriptions(
        IReadOnlyList<CodeDescriptionRule> schemaRules, CodeDescriptionRule? inclusionDescription, bool isList)
    {
        _schemaRules = schemaRules;
        _allRules = inclusionDescription is null ? schemaRules : schemaRules.Prepend(inclusionDescription).ToList();
        _isList = isList;
    }

    /// <summary>Reads <c>x-ingress.codeDescriptions</c> from the schema's root.</summary>
    /// <param name="inclusionDescription">
    /// The description part of <c>x-ingress.inclusion</c>. That rule reports its own errors.
    /// </param>
    public static CodeDescriptions From(JSchema schema, CodeDescriptionRule? inclusionDescription = null)
    {
        JToken? declared = schema.ExtensionData.TryGetValue(IngressExtension, out JToken? ingress) && ingress is JObject
            ? ingress[RulesKey]
            : null;

        return declared switch
        {
            null => new CodeDescriptions([], inclusionDescription, isList: true),
            JArray rules when rules.All(rule => rule is JObject) => new CodeDescriptions(
                rules.Cast<JObject>().Select(CodeDescriptionRule.Parse).ToList(), inclusionDescription, isList: true),
            _ => new CodeDescriptions([], inclusionDescription, isList: false)
        };
    }

    /// <summary>The reason the schema cannot honour its rules, or null.</summary>
    public string? ErrorFor(JSchema schema)
    {
        if (!_isList)
            return $"{RulesPath} must be a list of rules.";

        string? ruleError = _schemaRules
            .Select(rule => rule.ErrorFor(schema, RulesPath))
            .FirstOrDefault(error => error is not null);
        if (ruleError is not null)
            return ruleError;

        if (FieldWrittenAndRead() is { } chained)
            return $"{RulesPath} writes {chained}, a field that a rule reads.";

        string? writtenTwice = _allRules
            .GroupBy(rule => rule.DescriptionField, StringComparer.Ordinal)
            .FirstOrDefault(written => written.Count() > 1)?.Key;
        return writtenTwice is null ? null : $"{RulesPath} writes {writtenTwice} more than once.";
    }

    private string? FieldWrittenAndRead()
    {
        HashSet<string> readByAnyRule = _allRules.Select(rule => rule.Field).ToHashSet(StringComparer.Ordinal);
        HashSet<string> readBySchemaRules = _schemaRules.Select(rule => rule.Field).ToHashSet(StringComparer.Ordinal);

        return _schemaRules.Select(rule => rule.DescriptionField).FirstOrDefault(readByAnyRule.Contains)
            ?? _allRules.Select(rule => rule.DescriptionField).FirstOrDefault(readBySchemaRules.Contains);
    }

    /// <summary>Writes each rule's description on the record.</summary>
    public void Describe(JObject record)
    {
        foreach (CodeDescriptionRule rule in _allRules)
        {
            rule.Describe(record);
        }
    }
}
