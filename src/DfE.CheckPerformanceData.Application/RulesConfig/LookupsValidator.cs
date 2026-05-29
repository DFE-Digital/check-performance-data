using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed record LookupsValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static LookupsValidationResult Success() => new(true, Array.Empty<string>());
    public static LookupsValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}

/// <summary>
/// Validates a <see cref="Lookups"/> map before it replaces the live country-languages
/// blob. Errors map directly onto a GOV.UK error summary.
/// </summary>
public sealed class LookupsValidator
{
    public LookupsValidationResult Validate(Lookups lookups)
    {
        ArgumentNullException.ThrowIfNull(lookups);

        var errors = new List<string>();
        foreach (var (code, languages) in lookups.CountryLanguages)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add("Lookups has an empty country code.");
                continue;
            }
            if (languages.Count == 0)
            {
                errors.Add($"Country '{code}' has no languages.");
            }
            if (languages.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Country '{code}' has a blank language.");
            }
        }

        return errors.Count == 0 ? LookupsValidationResult.Success() : LookupsValidationResult.Failure(errors);
    }
}
