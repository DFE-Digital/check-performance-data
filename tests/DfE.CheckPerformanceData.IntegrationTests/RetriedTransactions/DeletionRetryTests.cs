using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: a deletion tracks one removed row and, through SaveChangesAsync, one audit row. A retry
// must not save the audit row of the failed attempt as well.
[Collection(nameof(PostgresCollection))]
public sealed class DeletionRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private sealed record SeededWindow(Guid WindowId, Guid FirstId, Guid SecondId);

    // Two exercises, the second replacing the first. Copied in minimal form from
    // ExerciseDeletionRepositoryTests.
    private async Task<SeededWindow> SeedWindowAsync()
    {
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified);
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "Retried delete",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = start,
            EndDate = end
        };
        var first = Exercise(start, end, replaces: null);
        var second = Exercise(start, end, replaces: first.Id);
        window.CheckingExercises.Add(first);
        window.CheckingExercises.Add(second);
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return new SeededWindow(window.Id, first.Id, second.Id);
    }

    private static CheckingExercise Exercise(DateTime start, DateTime end, Guid? replaces)
    {
        var datasetId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        return new CheckingExercise
        {
            Id = Guid.NewGuid(),
            ExerciseType = CheckingExerciseType.PupilData,
            TabName = "Students",
            StartDate = start,
            EndDate = end,
            ReplacesCheckingExerciseId = replaces,
            CurrentReleaseId = releaseId,
            Datasets = [new CheckingWindowDataset { Id = datasetId, Name = "students" }],
            Releases =
            [
                new CheckingExerciseRelease
                {
                    Id = releaseId,
                    Number = 1,
                    PublishedAt = start,
                    Files = [new CheckingExerciseReleaseFile { Id = Guid.NewGuid(), DatasetId = datasetId, DatasetName = "students" }]
                }
            ]
        };
    }

    [Fact]
    public async Task Exercise_delete_that_is_retried_after_a_transient_fault_writes_one_audit_row()
    {
        var seeded = await SeedWindowAsync();
        var interceptor = new FailFirstCommandInterceptor("DELETE FROM \"CheckingExercises\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        var deleted = await new ExerciseDeletionRepository(ctx).DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        Assert.NotNull(deleted);
        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.CheckingExercises.AnyAsync(e => e.Id == seeded.FirstId));
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(CheckingExercise) && a.Action == "Delete" && a.EntityId == seeded.FirstId.ToString()));
    }

    [Fact]
    public async Task Window_delete_that_is_retried_after_a_transient_fault_writes_one_audit_row()
    {
        var seeded = await SeedWindowAsync();
        var interceptor = new FailFirstCommandInterceptor("DELETE FROM \"CheckingWindows\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        var deleted = await new WindowDeletionRepository(ctx).DeleteAsync(seeded.WindowId, CancellationToken.None);

        Assert.True(deleted);
        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.CheckingWindows.AnyAsync(w => w.Id == seeded.WindowId));
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(CheckingWindow) && a.Action == "Delete" && a.EntityId == seeded.WindowId.ToString()));
    }
}
