using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext)
{
    public static readonly Guid KeyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    public static readonly Guid ClosedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");
    public static readonly Guid Post16CheckingWindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    // AB#298317: a 16-19 window whose pupil-data exercise closed yesterday while results enquiry
    // runs on — the state in which a school is told the window has closed and offered only the
    // results-enquiry question.
    public static readonly Guid ClosedPupilDataPost16CheckingWindowId = Guid.Parse("7D3F0B21-4C8E-4A9B-9F62-1E5A8C0D3B47");

    public async Task SeedAsync()
    {
        // Countries are seeded unconditionally on startup in every environment (see Program.cs),
        // idempotently via SeedCountries.ExecuteSeed. They are not window-specific, so they are
        // deliberately not part of the destructive dev/reset seed here.
        await SeedCheckingWindows.ExecuteSeed(dbContext, KeyStage4JuneCheckingWindowId, ClosedKeyStage4JuneCheckingWindowId, Post16CheckingWindowId, ClosedPupilDataPost16CheckingWindowId);

        // Pupil data is no longer stored in the database — it is seeded into blob storage
        // as per-school JSON by SeedPupilData (Web), which runs after this seeder.
    }
}
