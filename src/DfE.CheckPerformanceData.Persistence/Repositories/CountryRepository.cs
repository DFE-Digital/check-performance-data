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

    public async Task<string?> GetCodeByNameAsync(string name, CancellationToken cancellationToken = default)
        => await dbContext.Countries
            .Where(c => EF.Functions.ILike(c.Name, name))
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
}
