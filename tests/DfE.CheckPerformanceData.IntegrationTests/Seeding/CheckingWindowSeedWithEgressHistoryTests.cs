using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Seeding;

// The dev seeder wipes CheckingWindows at start-up wherever SeedDevelopmentData is on — local,
// the deployed DEV app and every review app. egress_runs references CheckingWindows with a
// RESTRICT foreign key, so a single egress run left behind (the E2E cleanup failing part-way is
// enough) made that wipe throw, the host terminated before it listened, and the pod never became
// Ready. On a review app that is invisible: the previous pod keeps serving, terraform times out
// waiting for the rollout, a re-run reports "No changes", and the health check passes against the
// old image. PR #441's review app served the 15 Sep image for two days this way.
//
// So the seed must take the egress history down with the windows it replaces — the same way it
// already takes the change requests. Against real Postgres because the failure is the database's
// referential action, which nothing in-memory would reproduce.
[Collection(nameof(PostgresCollection))]
public sealed class CheckingWindowSeedWithEgressHistoryTests(PostgresFixture fixture)
{
    private static readonly Guid WindowId = Guid.Parse("C1E5D9A2-7B3F-4E60-9D84-2A6F1B7C3E95");

    [Fact]
    public async Task Reseeding_the_windows_succeeds_when_an_egress_run_references_one_of_them()
    {
        var runId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            if (!await db.CheckingWindows.AnyAsync(w => w.Id == WindowId))
            {
                db.CheckingWindows.Add(new CheckingWindow
                {
                    Id = WindowId, Title = "Window with egress history", KeyStage = KeyStages.KS4,
                    CheckingWindowType = CheckingWindowType.KS4June,
                    StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
                });
                await db.SaveChangesAsync();
            }
            db.EgressRuns.Add(new EgressRun
            {
                Id = runId, WindowId = WindowId, Status = EgressRunStatus.Preprocessed,
                StartedById = Guid.NewGuid(), StartedByName = "Left behind", StartedAtUtc = DateTime.UtcNow
            });
            db.EgressRunOutputs.Add(new EgressRunOutput
            {
                Id = Guid.NewGuid(), RunId = runId, WindowId = WindowId, OutputType = EgressOutputType.RemoveLearners,
                IsActive = true, RawRecordsJson = "[]", SourceRecordCount = 0,
                FileName = "CYPMD_LDS_KS4_RemoveLearners_2026_09_17.csv"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            await SeedCheckingWindows.ExecuteSeed(db,
                DevDataSeeder.KeyStage4JuneCheckingWindowId, DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId,
                DevDataSeeder.Post16OctoberCheckingWindowId, DevDataSeeder.Post16NovemberCheckingWindowId,
                DevDataSeeder.Post16FebruaryCheckingWindowId, DevDataSeeder.Post16MarchCheckingWindowId);
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.EgressRuns.AnyAsync(r => r.Id == runId), "the egress run survived the reseed");
            Assert.False(await db.CheckingWindows.AnyAsync(w => w.Id == WindowId), "the old window survived the reseed");
            Assert.True(await db.CheckingWindows.AnyAsync(w => w.Id == DevDataSeeder.KeyStage4JuneCheckingWindowId));
        }
    }
}
