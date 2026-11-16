using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The words for the codes of one field. A schema gives one rule for each described field:
/// <c>"x-ingress": { "codeDescriptions": [ { "field": "R_INCL", "descriptionField":
/// "R_INCL_DESCRIPTION", "descriptions": { "51": "..." } } ] }</c> at its root. The description
/// part of <c>x-ingress.inclusion</c> has the same three keys. Codes are compared as trimmed text.
/// </summary>
internal sealed record CodeDescriptionRule(
    string Field, string DescriptionField, IReadOnlyDictionary<string, string> Descriptions)
{
    private const string FieldKey = "field";
    private const string DescriptionFieldKey = "descriptionField";
    private const string DescriptionsKey = "descriptions";

    public static CodeDescriptionRule Parse(JObject rule)
    {
        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        foreach (JProperty words in (rule[DescriptionsKey] as JObject ?? []).Properties())
        {
            descriptions[words.Name.Trim()] = words.Value.ToString();
        }

        return new CodeDescriptionRule(Text(rule[FieldKey]), Text(rule[DescriptionFieldKey]), descriptions);
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
            return $"{path} has a rule for {Field} that names no descriptionField.";
        if (!schema.Properties.TryGetValue(DescriptionField, out JSchema? target))
            return $"{path} writes {DescriptionField}, which the schema does not declare.";
        if (DescriptionField == Field)
            return $"{path} has a rule that writes {Field}, the field it reads.";
        if (target.Type is { } type && !type.HasFlag(JSchemaType.String))
            return $"{path} writes {DescriptionField}, which the schema does not declare as text.";
        if (Descriptions.Count == 0)
            return $"{path} has a rule for {Field} that gives no descriptions.";
        if (Descriptions.ContainsKey(string.Empty))
            return $"{path} has a rule for {Field} that describes a blank code.";

        return null;
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
