using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// Deleting an exercise is real SQL against three kinds of key: SET NULL from change requests
// (which the repository deletes by hand instead), CASCADE down to datasets and releases, and
// RESTRICT from an exercise that replaces it. Only a real database says whether the order works.
[Collection(nameof(PostgresCollection))]
public sealed class ExerciseDeletionRepositoryTests(PostgresFixture fixture)
{
    private static readonly DateTime Start = DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Unspecified);
    private static readonly DateTime FirstEnd = DateTime.SpecifyKind(new DateTime(2026, 9, 30), DateTimeKind.Unspecified);
    private static readonly DateTime SecondStart = DateTime.SpecifyKind(new DateTime(2026, 7, 1), DateTimeKind.Unspecified);
    private static readonly DateTime SecondEnd = DateTime.SpecifyKind(new DateTime(2026, 8, 31), DateTimeKind.Unspecified);

    [Fact]
    public async Task Delete_removes_the_exercise_its_rows_and_its_requests()
    {
        var seeded = await SeedWindowAsync();
        await SeedRequestAsync(seeded.WindowId, seeded.FirstId, RequestStatus.Submitted);
        await SeedRequestAsync(seeded.WindowId, seeded.FirstId, RequestStatus.InProgress);

        var deleted = await Repository().DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        Assert.NotNull(deleted);
        Assert.True(deleted.UsesExerciseStorage);
        await using var ctx = fixture.CreateContext();
        Assert.False(await ctx.CheckingExercises.AnyAsync(e => e.Id == seeded.FirstId));
        Assert.False(await ctx.Set<CheckingWindowDataset>().AnyAsync(d => d.CheckingExerciseId == seeded.FirstId));
        Assert.False(await ctx.Set<CheckingExerciseRelease>().AnyAsync(r => r.CheckingExerciseId == seeded.FirstId));
        Assert.False(await ctx.ChangeRequests.AnyAsync(r => r.CheckingExerciseId == seeded.FirstId));
    }

    [Fact]
    public async Task Delete_keeps_the_window_the_other_exercise_and_its_requests()
    {
        var seeded = await SeedWindowAsync();
        await SeedRequestAsync(seeded.WindowId, seeded.SecondId, RequestStatus.Submitted);

        await Repository().DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        var second = await ctx.CheckingExercises.SingleAsync(e => e.Id == seeded.SecondId);
        Assert.Null(second.ReplacesCheckingExerciseId);
        Assert.True(await ctx.Set<CheckingExerciseRelease>().AnyAsync(r => r.CheckingExerciseId == seeded.SecondId));
        Assert.True(await ctx.ChangeRequests.AnyAsync(r => r.CheckingExerciseId == seeded.SecondId));
        Assert.True(await ctx.CheckingWindows.AnyAsync(w => w.Id == seeded.WindowId));
    }

    [Fact]
    public async Task Delete_sets_the_window_dates_from_the_exercises_that_are_left()
    {
        var seeded = await SeedWindowAsync();

        await Repository().DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        var window = await ctx.CheckingWindows.SingleAsync(w => w.Id == seeded.WindowId);
        Assert.Equal(SecondStart, window.StartDate);
        Assert.Equal(SecondEnd, window.EndDate);
    }

    [Fact]
    public async Task Delete_of_the_last_exercise_keeps_the_window_dates()
    {
        var seeded = await SeedWindowAsync();
        await Repository().DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        Assert.NotNull(await Repository().DeleteAsync(seeded.WindowId, seeded.SecondId, CancellationToken.None));

        await using var ctx = fixture.CreateContext();
        var window = await ctx.CheckingWindows.Include(w => w.CheckingExercises).SingleAsync(w => w.Id == seeded.WindowId);
        Assert.Empty(window.CheckingExercises);
        Assert.Equal(SecondStart, window.StartDate);
        Assert.Equal(SecondEnd, window.EndDate);
    }

    [Fact]
    public async Task Delete_through_another_window_returns_null_and_deletes_nothing()
    {
        var seeded = await SeedWindowAsync();
        var other = await SeedWindowAsync();

        Assert.Null(await Repository().DeleteAsync(other.WindowId, seeded.FirstId, CancellationToken.None));

        await using var ctx = fixture.CreateContext();
        Assert.True(await ctx.CheckingExercises.AnyAsync(e => e.Id == seeded.FirstId));
    }

    [Fact]
    public async Task Delete_writes_an_audit_entry_for_the_exercise()
    {
        var seeded = await SeedWindowAsync();

        await Repository().DeleteAsync(seeded.WindowId, seeded.FirstId, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        Assert.True(await ctx.AuditEntries.AnyAsync(a =>
            a.EntityType == nameof(CheckingExercise) && a.Action == "Delete" && a.EntityId == seeded.FirstId.ToString()));
    }

    [Fact]
    public async Task Counts_are_scoped_to_the_exercise()
    {
        var seeded = await SeedWindowAsync();
        await SeedRequestAsync(seeded.WindowId, seeded.FirstId, RequestStatus.Submitted);
        await SeedRequestAsync(seeded.WindowId, seeded.FirstId, RequestStatus.InProgress);
        await SeedRequestAsync(seeded.WindowId, seeded.SecondId, RequestStatus.Submitted);

        var counts = await Repository().CountRequestsByStatusAsync(seeded.FirstId, CancellationToken.None);

        Assert.Equal(1, counts[RequestStatus.Submitted]);
        Assert.Equal(1, counts[RequestStatus.InProgress]);
        Assert.Equal(2, counts.Count);
    }

    private ExerciseDeletionRepository Repository() => new(fixture.CreateContext());

    private sealed record SeededWindow(Guid WindowId, Guid FirstId, Guid SecondId);

    // Two exercises, the second replacing the first — the RESTRICT pointer a plain delete cannot
    // get past — each with a dataset and a published release.
    private async Task<SeededWindow> SeedWindowAsync()
    {
        await using var ctx = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "Exercise delete",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = Start,
            EndDate = FirstEnd
        };

        var first = Exercise(Start, FirstEnd, replaces: null);
        var second = Exercise(SecondStart, SecondEnd, replaces: first.Id);
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

    private Task SeedRequestAsync(Guid windowId, Guid exerciseId, RequestStatus status) =>
        new RequestRepository(fixture.CreateContext()).UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            CheckingExerciseId = exerciseId,
            ReferenceNumber = $"REF-{Guid.NewGuid():N}"[..20],
            OrganisationUrn = 100000,
            PupilId = Guid.NewGuid(),
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = status,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove"
        });
}
