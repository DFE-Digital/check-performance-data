using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public static class SeedCheckingWindows
{
    public static async Task ExecuteSeed(IPortalDbContext dbContext, Guid openKs4WindowId, Guid closedKs4WindowId)
    {
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var openKs4JuneWindow = new CheckingWindow
        {
            Id = openKs4WindowId,
            StartDate = DateTime.Now.AddDays(-1),
            EndDate = DateTime.Now.AddDays(+13).Date.AddHours(17),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "Key Stage 4 June"
        };

        var closedKs4JuneWindow = new CheckingWindow
        {
            Id = closedKs4WindowId,
            StartDate = DateTime.Now.AddYears(-1).AddDays(-1),
            EndDate = DateTime.Now.AddYears(-1).AddDays(+13).Date.AddHours(17),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "KS4 June"
        };
        
        await dbContext.CheckingWindows.AddRangeAsync(
            openKs4JuneWindow,
            // new CheckingWindow
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddMonths(1),
            //     EndDate = DateTime.Now.AddMonths(1).AddDays(+14).Date.AddHours(17),
            //     KeyStage = KeyStages.KS4,
            //     Title = "KS4 Autumn"
            // },
            // new CheckingWindow
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddDays(-3),
            //     EndDate = DateTime.Now.AddDays(+11).Date.AddHours(17),
            //     KeyStage = KeyStages.KS2,
            //     Title = "KS2"
            // },
            // new CheckingWindow()
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddDays(-4),
            //     EndDate = DateTime.Now.AddDays(+14).Date.AddHours(17),
            //     KeyStage = KeyStages.Post16,
            //     Title = "16-18"
            // },
            // new CheckingWindow()
            // {
            //     Id = Guid.NewGuid(),
            //     StartDate = DateTime.Now.AddYears(-1).AddDays(-2),
            //     EndDate = DateTime.Now.AddYears(-1).AddDays(+12).Date.AddHours(17),
            //     KeyStage = KeyStages.Post16,
            //     Title = "16-18"
            // },
            closedKs4JuneWindow
            
        );
        
        await dbContext.SaveChangesAsync();
    }
}