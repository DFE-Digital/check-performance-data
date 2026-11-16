using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The words for the codes of one field. A schema gives one rule for each described field:
/// <c>"x-ingress": { "codeDescriptions": [ { "field": "R_INCL", "descriptionField":
/// "R_INCL_DESCRIPTION", "descriptions": { "51": "..." } } ] }</c> at its root. The description
/// part of <c>x-ingress.inclusion</c> has the same three keys. Codes are compared as trimmed text.
/// The words go into a CSV that a school opens in a spreadsheet, so they must not start with a
/// formula character.
/// </summary>
internal sealed record CodeDescriptionRule(
    string Field, string DescriptionField, IReadOnlyDictionary<string, string> Descriptions)
{
    internal const string DescriptionFieldKey = "descriptionField";
    private const string FieldKey = "field";
    private const string DescriptionsKey = "descriptions";
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>A code the schema describes more than once when codes are trimmed, or null.</summary>
    public string? RepeatedCode { get; private init; }

    /// <summary>A code whose description is not text or is blank, or null.</summary>
    public string? CodeWithoutWords { get; private init; }

    public static CodeDescriptionRule Parse(JObject rule)
    {
        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        string? repeatedCode = null;
        string? codeWithoutWords = null;
        foreach (JProperty words in (rule[DescriptionsKey] as JObject ?? []).Properties())
        {
            string code = words.Name.Trim();
            if (descriptions.ContainsKey(code))
                repeatedCode ??= code;
            if (words.Value is not JValue { Type: JTokenType.String } || string.IsNullOrWhiteSpace(words.Value.ToString()))
                codeWithoutWords ??= code;

            descriptions[code] = words.Value.ToString();
        }

        return new CodeDescriptionRule(Text(rule[FieldKey]), Text(rule[DescriptionFieldKey]), descriptions)
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
            return $"{path} has a rule for {Field} that names no descriptionField.";
        if (!schema.Properties.TryGetValue(DescriptionField, out JSchema? target))
            return $"{path} writes {DescriptionField}, which the schema does not declare.";
        if (target.Type is { } type && !type.HasFlag(JSchemaType.String))
            return $"{path} writes {DescriptionField}, which the schema does not declare as text.";

        return DescriptionsErrorFor(path);
    }

    private string? DescriptionsErrorFor(string path)
    {
        if (Descriptions.Count == 0)
            return $"{path} has a rule for {Field} that gives no descriptions.";
        if (Descriptions.ContainsKey(string.Empty))
            return $"{path} has a rule for {Field} that describes a blank code.";
        if (RepeatedCode is not null)
            return $"{path} has a rule for {Field} that describes {RepeatedCode} more than once.";
        if (CodeWithoutWords is not null)
            return $"{path} has a rule for {Field} that gives no words for {CodeWithoutWords}.";

        string? formulaCode = Descriptions
            .Where(words => words.Value.Length > 0 && FormulaPrefixes.Contains(words.Value[0]))
            .Select(words => words.Key)
            .FirstOrDefault();
        if (formulaCode is not null)
            return $"{path} has a rule for {Field} whose words for {formulaCode} start with a character that a spreadsheet reads as a formula.";

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
