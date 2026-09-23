using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #466: CheckingDataCatalogue reads every visible exercise across every window. "Visible" means it
// draws a tab (TabName set), is enabled, and now falls within [VisibleFrom, VisibleUntil) —
// VisibleUntil is exclusive, matching CheckingDataExercise.IsVisible. The Id tie-break in the
// ordering is what keeps two tabs sharing a TabOrder in the same order across requests.
[Collection(nameof(PostgresCollection))]
public sealed class CheckingDataCatalogueTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Guid _windowId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly TimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private async Task SeedAsync(params CheckingExercise[] exercises)
    {
        await using var seedCtx = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = _windowId, Title = "16-19 2026", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = Now.AddDays(-10), EndDate = Now.AddDays(10)
        };
        foreach (var exercise in exercises)
        {
            exercise.CheckingWindowId = _windowId;
            exercise.StartDate = Now.AddDays(-5);
            exercise.EndDate = Now.AddDays(5);
            window.CheckingExercises.Add(exercise);
        }
        seedCtx.CheckingWindows.Add(window);
        await seedCtx.SaveChangesAsync();
    }

    private static CheckingExercise Exercise(string? tabName, bool enabled = true,
        int tabOrder = 0, DateTime? until = null, Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            Name = "Student data", TabName = tabName, TabOrder = tabOrder,
            IsEnabled = enabled, VisibleUntil = until, UsesExerciseStorage = true
        };

    [Fact]
    public async Task ReturnsOnlyExercisesThatDrawATab()
    {
        // A row with no tab name was configured before #466. It has no tab and must not gain one.
        await SeedAsync(Exercise("Students"), Exercise(null));

        await using var ctx = fixture.CreateContext();
        var visible = await new CheckingDataCatalogue(ctx, _clock).GetVisibleAsync(default);

        Assert.Equal("Students", Assert.Single(visible).TabName);
    }

    [Fact]
    public async Task IgnoresDisabledAndExpiredExercises()
    {
        await SeedAsync(
            Exercise("Live"),
            Exercise("Switched off", enabled: false),
            Exercise("Finished", until: Now.AddDays(-1)));

        await using var ctx = fixture.CreateContext();
        var visible = await new CheckingDataCatalogue(ctx, _clock).GetVisibleAsync(default);

        Assert.Equal("Live", Assert.Single(visible).TabName);
    }

    [Fact]
    public async Task OrdersByTabOrderThenId()
    {
        // Id breaks the tie, so two tabs sharing an order come back the same way every request.
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await SeedAsync(
            Exercise("Summary", tabOrder: 1),
            Exercise("Second", tabOrder: 0, id: second),
            Exercise("First", tabOrder: 0, id: first));

        await using var ctx = fixture.CreateContext();
        var visible = await new CheckingDataCatalogue(ctx, _clock).GetVisibleAsync(default);

        Assert.Equal(["First", "Second", "Summary"], visible.Select(e => e.TabName));
    }
}
