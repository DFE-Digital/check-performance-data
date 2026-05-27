namespace DfE.CheckPerformanceData.Application.Countries;

public interface ICountryService
{
    Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
