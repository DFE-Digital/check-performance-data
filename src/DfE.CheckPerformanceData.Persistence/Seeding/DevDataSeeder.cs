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
                        Order = 0,
                        IsRequired = true,
                        Title = "New Pupil Details",
                        Explanation = "Please enter the details of the new pupil."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Add,
                        StepType = CheckingWindowStepType.FurtherDetails,
                        Order = 1,
                        IsRequired = false,
                        Title = "Further Details",
                        Explanation = "Please include any other information you think is important."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.Date,
                        Order = 0,
                        IsRequired = true,
                        Title = "Enter a date",
                        Explanation = "Some date or other."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.CheckBox,
                        Order = 1,
                        IsRequired = true,
                        Title = "Is this true?",
                        Explanation = "Check the box is this is true."
                    },
                    new CheckingWindowStep
                    {
                        RequestType = RequestTypes.Remove,
                        StepType = CheckingWindowStepType.FurtherDetails,
                        Order = 2,
                        IsRequired = false,
                        Title = "Some Further Details",
                        Explanation = "This is your last chance to talk."
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
    }
}