namespace DfE.CheckPerformanceData.Application.Countries;

public interface ICountryService
{
    Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Alpha-2 code for an exact (case-insensitive) country name match, or null.</summary>
    Task<string?> GetCodeByNameAsync(string name, CancellationToken cancellationToken = default);
}
