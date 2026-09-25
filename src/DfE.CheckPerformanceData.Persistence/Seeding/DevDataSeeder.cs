using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext)
{
    public static readonly Guid KeyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    public static readonly Guid ClosedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");

    // "16 to 19 Oct": the start of the results enquiry in October. The Web seed imports and
    // validates the October files (SeedPost16OctoberSamples).
    public static readonly Guid Post16OctoberCheckingWindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");

    // "16 to 19 Nov": October, then late results 2 added and validated (SeedPost16NovemberSamples).
    public static readonly Guid Post16NovemberCheckingWindowId = Guid.Parse("3B1D7C52-6E0A-4F8B-9C21-5A4E8D7F1B63");

    // "16 to 19 Feb": November, then the revised files added, the four files they replace retired,
    // and validated (SeedPost16FebruarySamples).
    public static readonly Guid Post16FebruaryCheckingWindowId = Guid.Parse("9E2A4C71-3B5D-4F60-8A17-C4D2E6F80B95");

    // "16 to 19 Mar": February, then included revised with retention added, included revised
    // retired, and validated (SeedPost16MarchSamples).
    public static readonly Guid Post16MarchCheckingWindowId = Guid.Parse("5C8F1E26-7A94-4D3B-B06E-2F9D4A1C7E58");

    public async Task SeedAsync()
    {
        // Countries are seeded unconditionally on startup in every environment (see Program.cs),
        // idempotently via SeedCountries.ExecuteSeed. They are not window-specific, so they are
        // deliberately not part of the destructive dev/reset seed here.
        await SeedCheckingWindows.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId, ClosedKeyStage4JuneCheckingWindowId,
            Post16OctoberCheckingWindowId, Post16NovemberCheckingWindowId, Post16FebruaryCheckingWindowId,
            Post16MarchCheckingWindowId);

        // Pupil data is no longer stored in the database — it is seeded into blob storage
        // as per-school JSON by SeedPupilData (Web), which runs after this seeder.
    }
}
