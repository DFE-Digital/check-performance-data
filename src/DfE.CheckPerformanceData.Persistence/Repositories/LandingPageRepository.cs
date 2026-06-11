using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class LandingPageRepository(
    IPortalDbContext dbContext,
    IPupilDataBlobClient pupilDataBlobClient) : ILandingPageRepository
{
    public async Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now, string laestab,
        CancellationToken cancellationToken)
    {
        var windows = await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.StartDate <= now && w.EndDate >= now)
            .Select(w => new
            {
                w.StartDate,
                w.EndDate,
                w.KeyStage,
                w.CheckingWindowType,
                w.Title,
                w.Id
            })
            .ToListAsync(cancellationToken);

        var result = new List<CheckingWindowDto>(windows.Count);
        foreach (var w in windows)
        {
            result.Add(new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                Id = w.Id,
                HasPupilData = await pupilDataBlobClient.HasPupilDataAsync(w.Id, laestab)
            });
        }

        return result;
    }
}
