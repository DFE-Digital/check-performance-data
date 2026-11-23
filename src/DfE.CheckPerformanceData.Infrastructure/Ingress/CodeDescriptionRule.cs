using DfE.CheckPerformanceData.Application.Common;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The words for the codes of one field. A schema gives one rule for each described field:
/// <c>"x-ingress": { "codeDescriptions": [ { "field": "R_INCL", "descriptionField":
/// "R_INCL_DESCRIPTION", "descriptions": { "51": "..." } } ] }</c> at its root. The description
/// part of <c>x-ingress.inclusion</c> has the same three keys. Codes are compared as trimmed text.
/// The words go into a CSV that a school opens in a spreadsheet, so they must not start with a
/// formula character. Both keys have this check.
/// </summary>
internal sealed record CodeDescriptionRule(
    string Field, string DescriptionField, IReadOnlyDictionary<string, string> Descriptions)
{
    /// <summary>A code the schema describes more than once when codes are trimmed, or null.</summary>
    public string? RepeatedCode { get; private init; }

    /// <summary>A code whose description is not text or is blank, or null.</summary>
    public string? CodeWithoutWords { get; private init; }

    public static CodeDescriptionRule Parse(JObject rule)
    {
        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        string? repeatedCode = null;
        string? codeWithoutWords = null;
        foreach (JProperty words in (rule[IngressSchemaKeys.Descriptions] as JObject ?? []).Properties())
        {
            string code = words.Name.Trim();
            if (descriptions.ContainsKey(code))
                repeatedCode ??= code;
            if (words.Value is not JValue { Type: JTokenType.String } || string.IsNullOrWhiteSpace(words.Value.ToString()))
                codeWithoutWords ??= code;

            descriptions[code] = words.Value.ToString();
        }

        return new CodeDescriptionRule(Text(rule[IngressSchemaKeys.Field]), Text(rule[IngressSchemaKeys.DescriptionField]), descriptions)
        {
            RepeatedCode = repeatedCode,
            CodeWithoutWords = codeWithoutWords
        };
    }

    /// <summary>The reason the schema cannot honour this rule, or null.</summary>
    /// <param name="path">The schema key that holds the rule, which the message names.</param>
    public string? ErrorFor(JSchema schema, string path)
    {
        if (string.IsNullOrWhiteSpace(Field))
            return $"{path} has a rule that names no field.";
        if (!schema.Properties.ContainsKey(Field))
            return $"{path} reads {Field}, which the schema does not declare.";
        if (string.IsNullOrWhiteSpace(DescriptionField))
            return $"{path} has a rule for {Field} that names no {IngressSchemaKeys.DescriptionField}.";
        if (!schema.Properties.TryGetValue(DescriptionField, out JSchema? target))
            return $"{path} writes {DescriptionField}, which the schema does not declare.";
        if (target.Type is { } type && !type.HasFlag(JSchemaType.String))
            return $"{path} writes {DescriptionField}, which the schema does not declare as text.";

        return DescriptionsErrorFor(path);
    }

    /// <summary>The reason ingress cannot use this rule's descriptions, or null.</summary>
    /// <param name="path">The schema key that holds the rule, which the message names.</param>
    internal string? DescriptionsErrorFor(string path)
    {
        if (Descriptions.Count == 0)
            return $"{path} gives no descriptions for {Field}.";
        if (Descriptions.ContainsKey(string.Empty))
            return $"{path} describes a blank {Field} code.";
        if (RepeatedCode is not null)
            return $"{path} describes the {Field} code {RepeatedCode} more than once.";
        if (CodeWithoutWords is not null)
            return $"{path} gives no words for the {Field} code {CodeWithoutWords}.";

        string? formulaCode = Descriptions
            .Where(words => SpreadsheetFormula.StartsWithTrigger(words.Value))
            .Select(words => words.Key)
            .FirstOrDefault();
        return formulaCode is null
            ? null
            : $"{path} gives words for the {Field} code {formulaCode} that start with a character a spreadsheet reads as a formula.";
    }

    /// <summary>
    /// Writes the words for the record's code. A code with no words leaves the description as it
    /// is, and so does a description the record already holds.
    /// </summary>
    public void Describe(JObject record)
    {
        if (!string.IsNullOrWhiteSpace(record[DescriptionField]?.ToString()))
            return;

        string code = record[Field] is { Type: not JTokenType.Null } value ? value.ToString().Trim() : string.Empty;
        if (Descriptions.GetValueOrDefault(code) is { Length: > 0 } words)
        {
            record[DescriptionField] = words;
        }
    }

    private static string Text(JToken? token) =>
        token is JValue { Value: not null } value ? value.ToString() : string.Empty;
}
