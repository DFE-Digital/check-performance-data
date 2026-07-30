namespace DfE.CheckPerformanceData.Application.Countries;

public interface ICountryRepository
{
    Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<string?> GetCodeByNameAsync(string name, CancellationToken cancellationToken = default);
}
