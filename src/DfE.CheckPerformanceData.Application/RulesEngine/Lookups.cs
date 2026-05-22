namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Reference data loaded alongside the rules. The country-languages map underpins
/// the <see cref="Predicate.OfficialLanguageIs"/> predicate, and lives in its own
/// blob so business users can update it independently of the rules document.
/// </summary>
/// <param name="CountryLanguages">
/// Country code (ISO 3166-1 alpha-2 or alpha-3) → list of official language names.
/// Lookup is case-insensitive on the language but exact on the country code.
/// </param>
public sealed record Lookups(IReadOnlyDictionary<string, IReadOnlyList<string>> CountryLanguages)
{
    public static readonly Lookups Empty = new(new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>Returns true iff the country is known and has the language as an official language.</summary>
    public bool CountryHasOfficialLanguage(string countryCode, string language)
    {
        if (!CountryLanguages.TryGetValue(countryCode, out var languages)) return false;
        foreach (var entry in languages)
        {
            if (string.Equals(entry, language, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
