using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public class DevDataSeeder(IPortalDbContext dbContext)
{
    public async Task SeedAsync()
    {
        await SeedCheckingWindows();

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedCheckingWindows()
    {
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var ks4JuneCheckingWindow = new CheckingWindow
        {
            Id = Guid.Parse("9A2949DD-BDE8-4DD6-ADC8-B8C6966D4EC1"),
            StartDate = DateTime.Now.AddDays(-1),
            EndDate = DateTime.Now.AddDays(+13).Date.AddHours(17),
            KeyStage = KeyStages.KS4,
            Title = "KS4 June"
        };
        
        await dbContext.CheckingWindows.AddRangeAsync(
            ks4JuneCheckingWindow,
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateTime.Now.AddDays(-1),
                EndDate = DateTime.Now.AddDays(+13).Date.AddHours(17),
                KeyStage = KeyStages.KS4,
                Title = "KS4 Autumn"
            },
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateTime.Now.AddDays(-3),
                EndDate = DateTime.Now.AddDays(+11).Date.AddHours(17),
                KeyStage = KeyStages.KS2,
                Title = "KS2"
            },
            new CheckingWindow()
            {
                Id = Guid.NewGuid(),
                StartDate = DateTime.Now.AddDays(-5),
                EndDate = DateTime.Now.AddDays(-5).AddDays(+14).Date.AddHours(17),
                KeyStage = KeyStages.Post16,
                Title = "16-18"
            }
        );
    }
}