using DfE.CheckPerformanceData.Application.BlobStorage;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext, IBlobContainerService containerService)
{
    private readonly Guid _keyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private readonly Guid _closedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");

    public async Task SeedAsync()
    {
        await SeedCheckingWindows.ExecuteSeed(dbContext, _keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId);
        await SeedPupils.ExecuteSeed(dbContext, [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);

        await containerService.EnsureContainerExistsAsync(_keyStage4JuneCheckingWindowId);
        await containerService.EnsureContainerExistsAsync(_closedKeyStage4JuneCheckingWindowId);
    }
}
