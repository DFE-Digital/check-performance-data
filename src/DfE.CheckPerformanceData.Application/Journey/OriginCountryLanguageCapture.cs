using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.RulesConfig;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class OriginCountryLanguageCapture(
    IRulesConfigService rulesConfigService,
    ICountryService countryService,
    ILogger<OriginCountryLanguageCapture> logger) : IOriginCountryLanguageCapture
{
    public const string OriginCountryQuestionId = "country-originally-from";

    public async Task ApplyAsync(RequestState journey, IReadOnlyDictionary<string, QuestionAnswer> newAnswers, CancellationToken cancellationToken = default)
    {
        if (!newAnswers.TryGetValue(OriginCountryQuestionId, out var answer))
            return;

        var code = answer.CodeValue;
        // The autocomplete only fills the hidden code field on an explicit selection,
        // so a re-POST of a previously answered page keeps the label but loses the
        // code. Recover it by exact name so both this check and the rules engine
        // (which reads CodeValue ?? TextValue) keep working.
        if (string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(answer.TextValue))
            code = await countryService.GetCodeByNameAsync(answer.TextValue.Trim(), cancellationToken);

        if (string.IsNullOrWhiteSpace(code))
        {
            journey.OriginCountryCode = null;
            journey.OriginCountryLanguages = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(answer.CodeValue))
            answer.CodeValue = code;

        journey.OriginCountryCode = code;
        journey.OriginCountryLanguages = await ResolveLanguagesAsync(code, cancellationToken);
    }

    // Null (→ evidence stays mandatory) when the code is absent from the lookup or
    // the lookup blob cannot be read — never fail the page POST over reference data.
    private async Task<List<string>?> ResolveLanguagesAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            var (lookups, _) = await rulesConfigService.GetLookupsAsync(cancellationToken);
            return lookups.CountryLanguages.TryGetValue(code, out var languages) ? languages.ToList() : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve official languages for country {Code}; evidence stays mandatory", code);
            return null;
        }
    }
}
