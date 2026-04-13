using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Seeding;

public class DevDataSeeder(IPortalDbContext dbContext)
{
    public async Task SeedAsync()
    {
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        await dbContext.CheckingWindows.AddRangeAsync(
            new CheckingWindow()
            {
                Id = 1,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(+13), 
                KeyStage = KeyStages.KS4, 
                Title = "KS4 June"
            },
            new CheckingWindow()
            {
                Id = 2,
                StartDate = DateTime.UtcNow.AddDays(-3),
                EndDate = DateTime.UtcNow.AddDays(+11), 
                KeyStage = KeyStages.KS2, 
                Title = "KS2"
            },
            new CheckingWindow()
            {
                Id = 3,
                StartDate = DateTime.UtcNow.AddDays(-5),
                EndDate = DateTime.UtcNow.AddDays(-5).AddDays(+14), 
                KeyStage = KeyStages.Post16, 
                Title = "16-18"
            }
        );
        
        await dbContext.SaveChangesAsync();
    }
}