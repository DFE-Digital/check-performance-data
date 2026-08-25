using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// The dev seed has to produce a window that runs two activities on two different date ranges, or
// none of #315-#320 can be developed or demoed locally. It also has to obey the rule that holds the
// model together: a window's outer dates are the union of its exercises' dates.
public sealed class SeededCheckingExerciseTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly Guid _openKs4 = Guid.NewGuid();
    private readonly Guid _closedKs4 = Guid.NewGuid();
    private readonly Guid _post16 = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        await SeedCheckingWindows.ExecuteSeed(ctx, _openKs4, _closedKs4, _post16);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task<CheckingWindow> LoadAsync(Guid windowId)
    {
        await using var ctx = CreateContext();
        return await ctx.CheckingWindows
            .Include(w => w.CheckingExercises)
            .AsNoTracking()
            .SingleAsync(w => w.Id == windowId);
    }

    [Fact]
    public async Task The_post16_window_runs_pupil_data_and_results_enquiry_on_different_ranges()
    {
        var window = await LoadAsync(_post16);

        var exercises = window.CheckingExercises.OrderBy(e => e.SortOrder).ToList();
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            exercises.Select(e => e.ExerciseType));
        Assert.True(
            exercises[1].EndDate > exercises[0].EndDate,
            "Results enquiry must outlast pupil data checking, or the multi-exercise shape is untestable.");
    }

    [Theory]
    [InlineData(0)] // open KS4 June
    [InlineData(1)] // closed KS4 June
    public async Task A_single_activity_window_gets_one_pupil_data_exercise_on_its_own_dates(int which)
    {
        var window = await LoadAsync(which == 0 ? _openKs4 : _closedKs4);

        var exercise = Assert.Single(window.CheckingExercises);
        Assert.Equal(CheckingExerciseType.PupilData, exercise.ExerciseType);
        Assert.Equal(window.StartDate, exercise.StartDate);
        Assert.Equal(window.EndDate, exercise.EndDate);
    }

    [Fact]
    public async Task Every_seeded_windows_outer_dates_equal_the_union_of_its_exercises()
    {
        foreach (var windowId in new[] { _openKs4, _closedKs4, _post16 })
        {
            var window = await LoadAsync(windowId);

            Assert.NotEmpty(window.CheckingExercises);
            Assert.Equal(window.StartDate, window.CheckingExercises.Min(e => e.StartDate));
            Assert.Equal(window.EndDate, window.CheckingExercises.Max(e => e.EndDate));
        }
    }
}
