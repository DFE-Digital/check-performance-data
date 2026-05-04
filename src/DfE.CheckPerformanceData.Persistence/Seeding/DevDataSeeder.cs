using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public class DevDataSeeder(IPortalDbContext dbContext)
{
    private readonly Guid KeyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    
    public async Task SeedAsync()
    {
        await SeedCheckingWindows.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId);
        await SeedPupils.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId);   
    }
}