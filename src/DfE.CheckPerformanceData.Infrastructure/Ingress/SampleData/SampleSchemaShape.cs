using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>#940: one property of a schema, as a sample data generator needs it.</summary>
public sealed record SampleSchemaField(
    string Name,
    JSchemaType Type,
    long? MaxLength,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? Enum,
    string? Pattern);

/// <summary>
/// #940: the schema that a generator makes rows for: its properties, and the codes of its
/// inclusion rule. A generator that knows its file can ignore it.
/// </summary>
/// <param name="InclusionField">The field of the inclusion code, or null.</param>
/// <param name="IncludedCodes">The codes that the inclusion rule includes. Empty when the rule does not decide.</param>
/// <param name="OtherCodes">The other codes that the rule describes.</param>
public sealed record SampleSchemaShape(
    IReadOnlyList<SampleSchemaField> Fields,
    string? InclusionField,
    IReadOnlyList<string> IncludedCodes,
    IReadOnlyList<string> OtherCodes)
{
    public static SampleSchemaShape Empty { get; } = new([], null, [], []);

    public static SampleSchemaShape From(JSchema schema)
    {
        var fields = schema.Properties.Select(p => new SampleSchemaField(
            p.Key,
            p.Value.Type ?? JSchemaType.None,
            p.Value.MaximumLength,
            p.Value.Minimum,
            p.Value.Maximum,
            p.Value.Enum.Count == 0 ? null : p.Value.Enum.Where(v => v.Type != JTokenType.Null).Select(v => v.ToString()).ToList(),
            p.Value.Pattern)).ToList();

        if (!schema.ExtensionData.TryGetValue(IngressSchemaKeys.Extension, out var ingress)
            || ingress[IngressSchemaKeys.Inclusion] is not JObject rule)
            return new SampleSchemaShape(fields, null, [], []);

        var included = (rule[IngressSchemaKeys.Included] as JArray ?? []).Select(c => c.ToString().Trim()).ToList();
        var described = (rule["descriptions"] as JObject)?.Properties().Select(p => p.Name.Trim()) ?? [];
        return new SampleSchemaShape(
            fields,
            rule[IngressSchemaKeys.Field]?.Value<string>(),
            included,
            described.Where(c => !included.Contains(c)).ToList());
    }
}
