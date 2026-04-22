using DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public class CheckYourPupilDataRepository(PortalDbContext dbContext) : ICheckYourPupilDataRepository
{
    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => await GetPageAsync(windowId, laestab, pincl: 200, search, page, pageSize);

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => await GetPageAsync(windowId, laestab, pincl: 400, search, page, pageSize);

    private async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetPageAsync(Guid windowId, string laestab, int pincl, string? search, int page, int pageSize)
    {
        var query = dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Laestab == laestab && p.Pincl == pincl);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.ILike(p.Firstname, $"%{search}%") ||
                                     EF.Functions.ILike(p.Surname, $"%{search}%"));

        query = query.OrderBy(p => p.Surname).ThenBy(p => p.Firstname);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(p => new PupilDto
            {
                Surname = p.Surname,
                Firstname = p.Firstname,
                Sex = p.Sex,
                DateOfBirth = p.DateOfBirth,
                Age = p.Age,
                FirstLanguage = p.FirstLanguage
            })
            .ToListAsync();

        return (items, totalCount);
    }
}
