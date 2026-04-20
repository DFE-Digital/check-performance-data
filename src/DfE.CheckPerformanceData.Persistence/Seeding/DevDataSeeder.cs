using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public class DevDataSeeder(PortalDbContext dbContext)
{
    public async Task SeedAsync()
    {
        await dbContext.CheckingWindows.ExecuteDeleteAsync();

        var ks4JuneCheckingWindow = new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+13)),
                KeyStage = KeyStages.KS4,
                Title = "KS4 June",
                CheckingWindowRequestSteps =
                [
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Add,
                        StepType = CheckingWindowStepType.NewLearner,
                        Order = 1,
                        IsRequired = true
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Add,
                        StepType = CheckingWindowStepType.FurtherDetails,
                        Order = 2,
                        IsRequired = false
                    },
                ]
            };

        await dbContext.CheckingWindows.AddRangeAsync(
            ks4JuneCheckingWindow,
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+13)),
                KeyStage = KeyStages.KS4,
                Title = "KS4 Autumn"
            },
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+11)),
                KeyStage = KeyStages.KS2,
                Title = "KS2"
            },
            new CheckingWindow()
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5).AddDays(+14)),
                KeyStage = KeyStages.Post16,
                Title = "16-18"
            }
        );

        await dbContext.SaveChangesAsync();



        // foreach (var definition in BuildDefinitions())
        // {
        //     var exists = await dbContext.CheckingWindowDefinitions.AnyAsync(d =>
        //         d.Title == definition.Title &&
        //         d.RequestType == definition.RequestType);

        //     if (!exists)
        //         dbContext.CheckingWindowDefinitions.Add(definition);
        // }

        
    }
}