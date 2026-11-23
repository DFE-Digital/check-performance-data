using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>Every code description a schema asks ingress to write.</summary>
internal sealed class CodeDescriptions
{
    private readonly IReadOnlyList<CodeDescriptionRule> _schemaRules;
    private readonly CodeDescriptionRule? _inclusionDescription;
    private readonly IReadOnlyList<CodeDescriptionRule> _allRules;
    private readonly bool _isList;

    private CodeDescriptions(
        IReadOnlyList<CodeDescriptionRule> schemaRules, CodeDescriptionRule? inclusionDescription, bool isList)
    {
        _schemaRules = schemaRules;
        _inclusionDescription = inclusionDescription;
        _allRules = inclusionDescription is null ? schemaRules : schemaRules.Prepend(inclusionDescription).ToList();
        _isList = isList;
    }

    /// <summary>Reads <c>x-ingress.codeDescriptions</c> from the schema's root.</summary>
    /// <param name="inclusionDescription">
    /// The description part of <c>x-ingress.inclusion</c>. That rule reports its own errors. This
    /// type reports a conflict between it and a rule of the list.
    /// </param>
    public static CodeDescriptions From(JSchema schema, CodeDescriptionRule? inclusionDescription = null)
    {
        JToken? declared = schema.ExtensionData.TryGetValue(IngressSchemaKeys.Extension, out JToken? ingress) && ingress is JObject
            ? ingress[IngressSchemaKeys.CodeDescriptions]
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
            return $"{IngressSchemaKeys.CodeDescriptionsPath} must be a list of rules.";

        return _schemaRules.Select(rule => rule.ErrorFor(schema, IngressSchemaKeys.CodeDescriptionsPath)).FirstOrDefault(error => error is not null)
            ?? ConflictBetweenSchemaRules()
            ?? ConflictWithInclusionRule();
    }

    private string? ConflictBetweenSchemaRules()
    {
        HashSet<string> read = _schemaRules.Select(rule => rule.Field).ToHashSet(StringComparer.Ordinal);
        string? chained = _schemaRules.Select(rule => rule.DescriptionField).FirstOrDefault(read.Contains);
        if (chained is not null)
            return $"{IngressSchemaKeys.CodeDescriptionsPath} writes {chained}, a field that one of its rules reads.";

        string? writtenTwice = _schemaRules
            .GroupBy(rule => rule.DescriptionField, StringComparer.Ordinal)
            .FirstOrDefault(written => written.Count() > 1)?.Key;
        return writtenTwice is null ? null : $"{IngressSchemaKeys.CodeDescriptionsPath} writes {writtenTwice} more than once.";
    }

    private string? ConflictWithInclusionRule()
    {
        if (_inclusionDescription is not { } inclusion)
            return null;
        if (_schemaRules.Any(rule => rule.DescriptionField == inclusion.DescriptionField))
            return $"{IngressSchemaKeys.CodeDescriptionsPath} writes {inclusion.DescriptionField}, which {IngressSchemaKeys.InclusionPath} writes too.";
        if (_schemaRules.Any(rule => rule.DescriptionField == inclusion.Field))
            return $"{IngressSchemaKeys.CodeDescriptionsPath} writes {inclusion.Field}, the field that {IngressSchemaKeys.InclusionPath} reads.";
        if (_schemaRules.Any(rule => rule.Field == inclusion.DescriptionField))
            return $"{IngressSchemaKeys.CodeDescriptionsPath} reads {inclusion.DescriptionField}, the field that {IngressSchemaKeys.InclusionPath} writes.";

        return null;
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
