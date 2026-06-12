using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext)
{
    public static readonly Guid KeyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    public static readonly Guid ClosedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");

    public async Task SeedAsync()
    {
        await SeedCountries.ExecuteSeed(dbContext);

        await SeedCheckingWindows.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId, ClosedKeyStage4JuneCheckingWindowId);

        // Pupil data is no longer stored in the database — it is seeded into blob storage
        // as per-school JSON by SeedPupilData (Web), which runs after this seeder.
    }
}
