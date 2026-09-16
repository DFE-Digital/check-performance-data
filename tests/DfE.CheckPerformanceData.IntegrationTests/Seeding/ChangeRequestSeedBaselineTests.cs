using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Seeding;

// The reset behind /dev/queues/cleanup-e2e-requests, which the browser suite calls between
// journeys to put change requests back to a known state.
//
// It used to delete only references matching 'DEV-%', a prefix minted in exactly one place —
// the dev pipeline harness. A request submitted through a journey is referenced
// CYPMD_{type}_{id}, so the reset never touched one, and the endpoint answered {"deleted":0}
// while the caller believed state had been cleared. That is survivable for most journeys and
// fatal for the merge journey, whose duplicate guard refuses a student who already has a
// request: the suite passed the first time it ran against an environment and failed on every
// run after that, with a 30-second navigation timeout that named none of this.
//
// So these pin the contract the callers actually need — anything a test run created goes,
// the seeded fixtures stay — against a real database, because the predicate is SQL and the
// two traps in writing it are both SQL ones.
[Collection(nameof(PostgresCollection))]
public sealed class ChangeRequestSeedBaselineTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task<int> ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        return await SeedChangeRequests.ResetToSeedBaselineAsync(ctx);
    }

    // ChangeRequest.WindowId is a real foreign key, so the rows need a window to hang off. One
    // window of this class's own, rather than a seeded dev one, so nothing here depends on — or
    // disturbs — what another test put in the table.
    private static readonly Guid WindowId = Guid.Parse("D9E1A7C4-2B65-4F08-9A31-6C7E4D2B8F50");

    private async Task GivenRequestsAsync(params string[] referenceNumbers)
    {
        await using var ctx = _fixture.CreateContext();

        if (!await ctx.CheckingWindows.AnyAsync(w => w.Id == WindowId))
        {
            ctx.CheckingWindows.Add(new CheckingWindow
            {
                Id = WindowId,
                Title = "Change-request reset tests",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
            });
            await ctx.SaveChangesAsync();
        }

        await ctx.ChangeRequests.Where(r => r.WindowId == WindowId).ExecuteDeleteAsync();
        foreach (var reference in referenceNumbers)
            ctx.ChangeRequests.Add(Request(reference));
        await ctx.SaveChangesAsync();
    }

    private async Task<string[]> SurvivingReferencesAsync()
    {
        await using var ctx = _fixture.CreateContext();
        return await ctx.ChangeRequests
            .Where(r => r.WindowId == WindowId)
            .Select(r => r.ReferenceNumber)
            .OrderBy(r => r)
            .ToArrayAsync();
    }

    // The defect. A merge request submitted by the browser suite has to go, or the next run of
    // that suite is refused by the duplicate guard before it reaches the step under test.
    [Fact]
    public async Task Reset_DeletesARequestSubmittedThroughAJourney()
    {
        await GivenRequestsAsync("CYPMD_16to19_A1B2C3D");

        var deleted = await ResetAsync();

        Assert.Equal(1, deleted);
        Assert.Empty(await SurvivingReferencesAsync());
    }

    // The seeded Kingsmead rows are fixtures several screens and tests read, and they share the
    // CYPMD prefix with the requests above. Widening the old filter to match that prefix would
    // have deleted them, which is why the reset works from the baseline's own reference list
    // rather than from a pattern.
    [Fact]
    public async Task Reset_KeepsTheSeededBaselineRequests()
    {
        var seeded = SeedChangeRequests.SeededReferenceNumbers.OrderBy(r => r).ToArray();
        await GivenRequestsAsync(seeded);

        var deleted = await ResetAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(seeded, await SurvivingReferencesAsync());
    }

    // The behaviour the endpoint already had, kept: the dev pipeline harness writes DEV- rows
    // and nothing should start leaving them behind.
    [Fact]
    public async Task Reset_StillDeletesDevPipelineRequests()
    {
        await GivenRequestsAsync("DEV-0123456789ab");

        var deleted = await ResetAsync();

        Assert.Equal(1, deleted);
        Assert.Empty(await SurvivingReferencesAsync());
    }

    // Membership of the baseline, not a pattern that looks like it. '_' is a single-character
    // wildcard in SQL LIKE, so 'CYPMD_KS4June_SEED%' quietly matches references that only
    // resemble a seeded one — this row is exactly that shape and must still be deleted.
    [Fact]
    public async Task Reset_DeletesAReferenceThatMerelyResemblesASeededOne()
    {
        await GivenRequestsAsync("CYPMDxKS4JunexSEED001", "CYPMD_KS4June_SEED999");

        var deleted = await ResetAsync();

        Assert.Equal(2, deleted);
        Assert.Empty(await SurvivingReferencesAsync());
    }

    // The whole point, in one test: a run's leftovers go, the fixtures stay, in one pass.
    [Fact]
    public async Task Reset_LeavesExactlyTheBaseline_WhenBothArePresent()
    {
        var seeded = SeedChangeRequests.SeededReferenceNumbers.OrderBy(r => r).ToArray();
        await GivenRequestsAsync([.. seeded, "CYPMD_16to19_A1B2C3D", "DEV-0123456789ab"]);

        var deleted = await ResetAsync();

        Assert.Equal(2, deleted);
        Assert.Equal(seeded, await SurvivingReferencesAsync());
    }

    // Running it twice must not report work it did not do — the callers use the count to tell
    // whether the endpoint is wired up at all.
    [Fact]
    public async Task Reset_IsIdempotent()
    {
        await GivenRequestsAsync("CYPMD_16to19_A1B2C3D");

        await ResetAsync();
        var secondRun = await ResetAsync();

        Assert.Equal(0, secondRun);
    }

    private static ChangeRequest Request(string referenceNumber) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = WindowId,
        OrganisationUrn = 142313,
        // Unspecified kind: the column is "timestamp without time zone", and Npgsql refuses
        // to write a UTC-kinded value into one.
        Submitted = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Test",
        Status = RequestStatus.ReadyToSubmit,
        ReferenceNumber = referenceNumber,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove - test",
    };
}
