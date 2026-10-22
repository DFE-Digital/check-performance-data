using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #466: an exercise now has its own identity (Guid Id), so a window can hold several releases of
// one activity, or any number of typeless display-only shares. SyncExercises has to match an
// incoming DTO to its existing row by that id, not by ExerciseType, and a configured exercise
// dropped from the wizard must survive as a disabled row rather than being deleted, because its
// blobs and its change requests point at it.
[Collection(nameof(PostgresCollection))]
public sealed class WindowRepositoryExerciseIdentityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Guid _windowId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private async Task SeedWindowWithExercisesAsync(params (Guid Id, string TabName)[] exercises)
    {
        await using var seedCtx = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = _windowId,
            Title = "16 to 19",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(14)
        };

        foreach (var (id, tabName) in exercises)
        {
            window.CheckingExercises.Add(new CheckingExercise
            {
                Id = id,
                CheckingWindowId = _windowId,
                Name = tabName,
                TabName = tabName,
                IsEnabled = true,
                UsesExerciseStorage = true,
                DisplayOnly = true,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                Datasets =
                [
                    new CheckingWindowDataset
                    {
                        CheckingExerciseId = id,
                        CheckingWindowId = _windowId,
                        Name = $"{tabName}-dataset"
                    }
                ]
            });
        }

        seedCtx.CheckingWindows.Add(window);
        await seedCtx.SaveChangesAsync();
    }

    private async Task SeedWindowWithTypedExercisesAsync(
        CheckingExerciseType type, params (Guid Id, string TabName)[] exercises)
    {
        await using var seedCtx = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = _windowId,
            Title = "16 to 19",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(14)
        };

        foreach (var (id, tabName) in exercises)
        {
            window.CheckingExercises.Add(new CheckingExercise
            {
                Id = id,
                CheckingWindowId = _windowId,
                ExerciseType = type,
                Name = tabName,
                TabName = tabName,
                IsEnabled = true,
                UsesExerciseStorage = true,
                DisplayOnly = false,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                Datasets =
                [
                    new CheckingWindowDataset
                    {
                        CheckingExerciseId = id,
                        CheckingWindowId = _windowId,
                        Name = $"{tabName}-dataset"
                    }
                ]
            });
        }

        seedCtx.CheckingWindows.Add(window);
        await seedCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateAsync_MatchesAnExistingExerciseById_NotByType()
    {
        // Two releases of the same activity in one window. Matching by type would collapse them.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await SeedWindowWithExercisesAsync((first, "Autumn release"), (second, "Revised release"));

        await using (var ctx = fixture.CreateContext())
        {
            var repository = new WindowRepository(ctx);
            var window = await repository.GetByIdAsync(_windowId, CancellationToken.None);
            window!.Exercises.Single(e => e.Id == second).TabName = "Revised";
            await repository.UpdateAsync(window, CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var reloaded = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);
            Assert.Equal(2, reloaded!.Exercises.Count);
            Assert.Equal("Autumn release", reloaded.Exercises.Single(e => e.Id == first).Name);
            Assert.Equal("Revised", reloaded.Exercises.Single(e => e.Id == second).TabName);
        }
    }

    [Fact]
    public async Task UpdateAsync_DisablesAConfiguredExerciseRatherThanDeletingIt()
    {
        // A release that has been given files and a tab is history. Dropping it from the wizard
        // must not destroy the row that the school's blobs and change requests point at.
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        await SeedWindowWithExercisesAsync((kept, "Kept"), (dropped, "Dropped"));

        await using (var ctx = fixture.CreateContext())
        {
            var repository = new WindowRepository(ctx);
            var window = await repository.GetByIdAsync(_windowId, CancellationToken.None);
            window!.Exercises.RemoveAll(e => e.Id == dropped);
            await repository.UpdateAsync(window, CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var reloaded = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);
            var survivor = reloaded!.Exercises.SingleOrDefault(e => e.Id == dropped);
            Assert.NotNull(survivor);
            Assert.False(survivor!.IsEnabled);
        }
    }

    [Fact]
    public async Task UpdateAsync_KeepsTwoReleasesOfOneExerciseType()
    {
        // #466's actual reason to exist: the unique index on (CheckingWindowId, ExerciseType) is
        // now filtered to UsesExerciseStorage = false, so two ResultsEnquiry rows on the new
        // exercise-id storage are legal in one window — an autumn release and a revised one. Id
        // matching is what keeps them apart on update; type matching would collapse them.
        var autumn = Guid.NewGuid();
        var revised = Guid.NewGuid();
        await SeedWindowWithTypedExercisesAsync(
            CheckingExerciseType.ResultsEnquiry, (autumn, "Autumn"), (revised, "Revised"));

        await using (var ctx = fixture.CreateContext())
        {
            var repository = new WindowRepository(ctx);
            var window = await repository.GetByIdAsync(_windowId, CancellationToken.None);
            window!.Exercises.Single(e => e.Id == revised).TabName = "Revised (updated)";
            await repository.UpdateAsync(window, CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var reloaded = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);
            Assert.Equal(2, reloaded!.Exercises.Count);
            Assert.Equal("Autumn", reloaded.Exercises.Single(e => e.Id == autumn).TabName);
            Assert.Equal("Revised (updated)", reloaded.Exercises.Single(e => e.Id == revised).TabName);
            Assert.All(reloaded.Exercises, e => Assert.Equal(CheckingExerciseType.ResultsEnquiry, e.ExerciseType));
        }
    }
}
