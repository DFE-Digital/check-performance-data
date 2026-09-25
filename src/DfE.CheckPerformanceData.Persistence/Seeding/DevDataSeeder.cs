using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext)
{
    public static readonly Guid KeyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    public static readonly Guid ClosedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");

    // "16 to 19 Oct", set up for the start of the results enquiry in October. It has its exercises
    // and dataset slots but no data. An admin imports the sample files (SeedPost16OctoberSamples)
    // and validates, as they would the supplier's files.
    public static readonly Guid Post16OctoberCheckingWindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");

    // "16 to 19 Nov": the same setup as October, but the seed imports and validates the October
    // files (SeedPost16NovemberSamples). Only the late results 2 slot is empty, for an admin to fill.
    public static readonly Guid Post16NovemberCheckingWindowId = Guid.Parse("3B1D7C52-6E0A-4F8B-9C21-5A4E8D7F1B63");

    public async Task SeedAsync()
    {
        // Countries are seeded unconditionally on startup in every environment (see Program.cs),
        // idempotently via SeedCountries.ExecuteSeed. They are not window-specific, so they are
        // deliberately not part of the destructive dev/reset seed here.
        await SeedCheckingWindows.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId, ClosedKeyStage4JuneCheckingWindowId,
            Post16OctoberCheckingWindowId, Post16NovemberCheckingWindowId);

        // Pupil data is no longer stored in the database — it is seeded into blob storage
        // as per-school JSON by SeedPupilData (Web), which runs after this seeder.
    }
}
