using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Features.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public class LandingPageRepository(PortalDbContext dbContext) : ILandingPageRepository
{
    public async Task<List<OpenCheckingWindowDto>> GetOpenWindowsAsync(DateTimeOffset now, IEnumerable<KeyStages> organisationKeyStages, 
        CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(window
                => window.StartDate <= now
                   && window.EndDate >= now
                   && organisationKeyStages.Contains(window.KeyStage)
            )
            .Select(w => new OpenCheckingWindowDto()
                { EndDate = w.EndDate, KeyStage = w.KeyStage, Title = w.Title })
            .ToListAsync(cancellationToken);
}