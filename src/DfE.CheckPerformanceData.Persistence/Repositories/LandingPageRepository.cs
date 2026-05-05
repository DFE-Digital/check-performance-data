using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public class LandingPageRepository(PortalDbContext dbContext) : ILandingPageRepository
{
    public async Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now,
        IEnumerable<KeyStages> organisationKeyStages, string laestab,
        CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(window
                => window.StartDate <= now
                   && window.EndDate >= now
                   && organisationKeyStages.Contains(window.KeyStage)
            )
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                Title = w.Title,
                Id = w.Id,
                HasPupilData = dbContext.Pupils.Any(p => p.CheckingWindowId == w.Id && p.Laestab == laestab)
            })
            .ToListAsync(cancellationToken);

    public async Task<List<CheckingWindowDto>> GetClosedWindowsAsync(DateTime now,
        IEnumerable<KeyStages> organisationKeyStages, string laestab, CancellationToken cancellationToken)
        =>
            await dbContext.CheckingWindows
                .AsNoTracking()
                .Where(window
                    => window.EndDate >= now.AddMonths(-12)
                       && window.EndDate < now
                       && organisationKeyStages.Contains(window.KeyStage)
                )
                .Select(w => new CheckingWindowDto
                {
                    StartDate = w.StartDate,
                    EndDate = w.EndDate,
                    KeyStage = w.KeyStage, 
                    Title = w.Title, 
                    Id = w.Id,
                    HasPupilData = dbContext.Pupils.Any(p => p.CheckingWindowId == w.Id && p.Laestab == laestab)
                })
                .ToListAsync(cancellationToken);
}