namespace DfE.CheckPerformanceData.Application.Countries;

public sealed class CountryService(ICountryRepository repository) : ICountryService
{
    public Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => repository.SearchAsync(query, cancellationToken);
}
