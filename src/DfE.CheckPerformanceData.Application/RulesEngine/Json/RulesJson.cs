using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.RulesEngine.Json;

/// <summary>
/// Single source of truth for JSON options used to (de)serialise <see cref="RuleSet"/>
/// and <see cref="Lookups"/>. Centralised so every caller picks up the right
/// converters and casing.
/// </summary>
public static class RulesJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        opts.Converters.Add(new PredicateJsonConverter());
        opts.Converters.Add(new FieldValueJsonConverter());
        return opts;
    }
}
