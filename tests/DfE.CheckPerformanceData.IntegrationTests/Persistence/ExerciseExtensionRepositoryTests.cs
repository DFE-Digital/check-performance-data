using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AB#307773: the extension tables and (from Task 4) the guarded writes behind them.
[Collection(nameof(PostgresCollection))]
public sealed class ExerciseExtensionRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime ExerciseEnd = new(2026, 10, 1, 17, 0, 0);
    private static readonly DateTime ExtensionEnd = new(2026, 10, 8, 17, 0, 0);

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId, Title = "KS4 June", KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June, StartDate = Start, EndDate = ExerciseEnd,
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = _exerciseId, CheckingWindowId = _windowId, ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = Start, EndDate = ExerciseEnd, TabOrder = 0, TabName = "Pupils", IsEnabled = true
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

    private static CheckingExerciseExtension Extension(Guid exerciseId, DateTime end, params string[] laestabs) => new()
    {
        CheckingExerciseId = exerciseId, EndDate = end, CreatedAt = new DateTime(2026, 9, 20, 9, 0, 0), CreatedBy = "Ada Admin",
        Schools = laestabs.Select(l => new CheckingExerciseExtensionSchool { CheckingExerciseId = exerciseId, Laestab = l }).ToList()
    };

    [Fact]
    public async Task An_extension_and_its_schools_round_trip_onto_the_window_dto()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290", "8604070"));
            await ctx.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var window = await new WindowRepository(read).GetByIdAsync(_windowId, CancellationToken.None);

        var extension = Assert.Single(window!.Exercises.Single().Extensions);
        Assert.Equal(ExtensionEnd, extension.EndDate);
        Assert.Equal(["8604070", "9334290"], extension.Laestabs);
        Assert.Equal("Ada Admin", extension.CreatedBy);
        Assert.Null(extension.RequestsSentAt);
    }

    [Fact]
    public async Task A_school_cannot_be_in_two_extensions_of_one_exercise()
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290"));
        ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd.AddDays(1), "9334290"));

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_the_exercise_deletes_its_extensions_and_schools()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290"));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
            await new ExerciseDeletionRepository(ctx).DeleteAsync(_windowId, _exerciseId, CancellationToken.None);

        await using var check = fixture.CreateContext();
        Assert.Equal(0, await check.CheckingExerciseExtensions.CountAsync(x => x.CheckingExerciseId == _exerciseId));
        Assert.Equal(0, await check.CheckingExerciseExtensionSchools.CountAsync(x => x.CheckingExerciseId == _exerciseId));
    }
}
