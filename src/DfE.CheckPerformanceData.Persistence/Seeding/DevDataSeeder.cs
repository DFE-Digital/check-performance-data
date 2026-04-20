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

        await dbContext.CheckingWindows.AddRangeAsync(
            new CheckingWindow
            {
                Id = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(+13)),
                KeyStage = KeyStages.KS4,
                Title = "KS4 June"
            },
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

        foreach (var definition in BuildDefinitions())
        {
            var exists = await dbContext.CheckingWindowDefinitions.AnyAsync(d =>
                d.Title == definition.Title &&
                d.RequestType == definition.RequestType);

            if (!exists)
                dbContext.CheckingWindowDefinitions.Add(definition);
        }

        await dbContext.SaveChangesAsync();
    }

    private static List<CheckingWindowDefinition> BuildDefinitions() =>
    [
        BuildDefinition("KS2 2025 Add",
            CheckingWindowRequestType.Add,
            CheckingWindowType.KS2,
            [
                (CheckingWindowStepType.Date, "Date of Change"),
                (CheckingWindowStepType.FurtherDetails, "Reason for Addition"),
                (CheckingWindowStepType.EvidenceUpload, "Upload Evidence"),
                (CheckingWindowStepType.CheckBox, "Confirm Declaration"),
            ]),

        BuildDefinition("KS2 2025 Remove",
            CheckingWindowRequestType.Remove,
            CheckingWindowType.KS2,
            [
                (CheckingWindowStepType.Date, "Date of Change"),
                (CheckingWindowStepType.CheckBox, "Parental Consent"),
                (CheckingWindowStepType.EvidenceUpload, "Upload Evidence"),
                (CheckingWindowStepType.FurtherDetails, "Further Details"),
            ]),

        BuildDefinition("KS4 2025 Add",
            CheckingWindowRequestType.Add,
            CheckingWindowType.KS4June,
            [
                (CheckingWindowStepType.FurtherDetails, "Reason for Addition"),
                (CheckingWindowStepType.Date, "Date of Change"),
                (CheckingWindowStepType.EvidenceUpload, "Upload Evidence"),
                (CheckingWindowStepType.CheckBox, "Confirm Declaration"),
            ]),

        BuildDefinition("KS4 2025 Add",
            CheckingWindowRequestType.Add,
            CheckingWindowType.KS4Autumn,
            [
                (CheckingWindowStepType.FurtherDetails, "Reason for Addition"),
                (CheckingWindowStepType.Date, "Date of Change"),
                (CheckingWindowStepType.EvidenceUpload, "Upload Evidence"),
                (CheckingWindowStepType.CheckBox, "Confirm Declaration"),
            ]),
    ];

    private static CheckingWindowDefinition BuildDefinition(
        string title,
        CheckingWindowRequestType requestType,
        CheckingWindowType windowType,
        (CheckingWindowStepType StepType, string Title)[] steps)
    {
        var definitionId = Guid.NewGuid();

        return new CheckingWindowDefinition
        {
            Id = definitionId,
            Title = title,
            RequestType = requestType,
            WindowType = windowType,
            Steps = steps.Select((s, index) => new CheckingWindowStep
            {
                Id = Guid.NewGuid(),
                CheckingWindowDefinitionId = definitionId,
                CheckingWindowStepType = s.StepType,
                Title = s.Title,
                Order = index,
                IsRequired = true
            }).ToList()
        };
    }
}