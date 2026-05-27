namespace DfE.CheckPerformanceData.Application.Countries;

public interface ICountryRepository
{
    Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
