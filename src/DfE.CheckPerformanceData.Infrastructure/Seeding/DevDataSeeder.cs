using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Seeding;

public class DevDataSeeder(IPortalDbContext dbContext)
{
    public async Task SeedAsync()
    {
        if (await dbContext.CheckingWindows.AnyAsync())
            return;

        await dbContext.CheckingWindows.AddRangeAsync(
            new CheckingWindow()
            {
                Id = 1,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(+13), 
                OrganisationType = OrganisationTypes.KS4, 
                Title = "KS4 June"
            },
            new CheckingWindow()
            {
                Id = 2,
                StartDate = DateTime.UtcNow.AddDays(-3),
                EndDate = DateTime.UtcNow.AddDays(+11), 
                OrganisationType = OrganisationTypes.KS2, 
                Title = "KS2"
            }
        );
        
        await dbContext.SaveChangesAsync();
    }
}