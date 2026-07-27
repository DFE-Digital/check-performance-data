using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class CountryRepository(IPortalDbContext dbContext) : ICountryRepository
{
    public async Task<IReadOnlyList<CountrySuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => await dbContext.Countries
            .Where(c => EF.Functions.ILike(c.Name, $"%{query}%"))
            .OrderBy(c => c.Name)
            .Take(10)
            .Select(c => new CountrySuggestionDto(c.Code, c.Name))
            .ToListAsync(cancellationToken);

    // Deliberately not ILike: the name is a posted form value, and ILike would read any '%'
    // or '_' in it as a wildcard, resolving an arbitrary country whose code is then stored on
    // the journey and sent to the rules engine. Compare case-insensitively instead.
    public async Task<string?> GetCodeByNameAsync(string name, CancellationToken cancellationToken = default)
        => await dbContext.Countries
            .Where(c => c.Name.ToLower() == name.ToLower())
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
}
