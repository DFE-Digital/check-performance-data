using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// The storage resolver asks this on every journey page, so it reads only the exercise rows of one
// kind, not the window's datasets and releases.
[Collection(nameof(PostgresCollection))]
public sealed class WindowRepositoryExercisesOfKindTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _resultsId = Guid.NewGuid();
    private readonly Guid _releaseId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        var start = DateTime.Today;
        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId,
            Title = "16 to 19",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = start,
            EndDate = start.AddDays(14),
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = Guid.NewGuid(), CheckingWindowId = _windowId,
                    ExerciseType = CheckingExerciseType.PupilData, TabName = "Students",
                    UsesExerciseStorage = true, IsEnabled = true, StartDate = start, EndDate = start.AddDays(7)
                },
                new CheckingExercise
                {
                    Id = _resultsId, CheckingWindowId = _windowId,
                    ExerciseType = CheckingExerciseType.ResultsEnquiry, TabName = "Results",
                    UsesExerciseStorage = true, IsEnabled = true, ShowLateResultsWarning = true,
                    VisibleUntil = start.AddDays(30), CurrentReleaseId = _releaseId,
                    StartDate = start, EndDate = start.AddDays(14)
                },
                new CheckingExercise
                {
                    Id = Guid.NewGuid(), CheckingWindowId = _windowId,
                    ExerciseType = null, TabName = "Summary", DisplayOnly = true,
                    UsesExerciseStorage = true, IsEnabled = true, StartDate = start, EndDate = start.AddDays(14)
                }
            ]
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Returns_only_the_exercise_of_that_kind_with_what_the_resolver_and_its_callers_read()
    {
        await using var ctx = fixture.CreateContext();

        var exercises = await new WindowRepository(ctx).GetExercisesOfKindAsync(
            _windowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None);

        var results = Assert.Single(exercises);
        Assert.Equal(_resultsId, results.Id);
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, results.ExerciseType);
        Assert.True(results.UsesExerciseStorage);
        Assert.True(results.IsEnabled);
        Assert.True(results.ShowLateResultsWarning);
        Assert.Equal(DateTime.Today.AddDays(30), results.VisibleUntil);
        Assert.Equal(_releaseId, results.CurrentReleaseId);
    }

    [Fact]
    public async Task An_unknown_window_has_none()
    {
        await using var ctx = fixture.CreateContext();

        Assert.Empty(await new WindowRepository(ctx).GetExercisesOfKindAsync(
            Guid.NewGuid(), CheckingExerciseType.PupilData, CancellationToken.None));
    }
}
