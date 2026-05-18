using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class LandingPageRepository(PortalDbContext dbContext) : ILandingPageRepository
{
    public async Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now, string urn,
        CancellationToken cancellationToken)
    {
        var windowsWithData = await GetWindowIdsWithPupilDataAsync(urn, cancellationToken);

        return await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.StartDate <= now && w.EndDate >= now)
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                Id = w.Id,
                HasPupilData = windowsWithData.Contains(w.Id)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> GetWindowIdsWithPupilDataAsync(string urn, CancellationToken cancellationToken)
        => (await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.Urn == urn)
            .Select(p => p.CheckingWindowId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
}
