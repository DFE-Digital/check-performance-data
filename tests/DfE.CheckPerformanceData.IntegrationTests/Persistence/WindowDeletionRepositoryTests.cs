using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// Deleting a window is real SQL against three kinds of key: RESTRICT from change requests and
// egress runs, CASCADE down to exercises, datasets and releases, and RESTRICT between two
// exercises of one window. Only a real database says whether the order in the repository works.
[Collection(nameof(PostgresCollection))]
public sealed class WindowDeletionRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Delete_removes_the_window_and_everything_that_belongs_to_it()
    {
        var doomed = await SeedWindowAsync("Doomed");
        await SeedRequestAsync(doomed.WindowId, doomed.PupilDataId, RequestStatus.SubmittedUnCommitted);
        await SeedRequestAsync(doomed.WindowId, doomed.PupilDataId, RequestStatus.InProgress);
        await SeedEgressRunAsync(doomed.WindowId);

        Assert.True(await Repository().DeleteAsync(doomed.WindowId, CancellationToken.None));

        await using var ctx = fixture.CreateContext();
        Assert.False(await ctx.CheckingWindows.AnyAsync(w => w.Id == doomed.WindowId));
        Assert.False(await ctx.CheckingExercises.AnyAsync(e => e.CheckingWindowId == doomed.WindowId));
        Assert.False(await ctx.Set<CheckingWindowDataset>().AnyAsync(d => doomed.ExerciseIds.Contains(d.CheckingExerciseId)));
        Assert.False(await ctx.Set<CheckingExerciseRelease>().AnyAsync(r => doomed.ExerciseIds.Contains(r.CheckingExerciseId)));
        Assert.False(await ctx.ChangeRequests.AnyAsync(r => r.WindowId == doomed.WindowId));
        Assert.False(await ctx.EgressRuns.AnyAsync(r => r.WindowId == doomed.WindowId));
        Assert.False(await ctx.EgressRunOutputs.AnyAsync(o => o.WindowId == doomed.WindowId));
    }

    [Fact]
    public async Task Delete_leaves_every_other_window_alone()
    {
        var doomed = await SeedWindowAsync("Doomed");
        var kept = await SeedWindowAsync("Kept");
        await SeedRequestAsync(kept.WindowId, kept.PupilDataId, RequestStatus.SubmittedUnCommitted);
        await SeedEgressRunAsync(kept.WindowId);

        await Repository().DeleteAsync(doomed.WindowId, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        Assert.True(await ctx.CheckingWindows.AnyAsync(w => w.Id == kept.WindowId));
        Assert.Equal(2, await ctx.CheckingExercises.CountAsync(e => e.CheckingWindowId == kept.WindowId));
        Assert.True(await ctx.Set<CheckingExerciseRelease>().AnyAsync(r => kept.ExerciseIds.Contains(r.CheckingExerciseId)));
        Assert.True(await ctx.ChangeRequests.AnyAsync(r => r.WindowId == kept.WindowId));
        Assert.True(await ctx.EgressRuns.AnyAsync(r => r.WindowId == kept.WindowId));
    }

    [Fact]
    public async Task Delete_of_an_unknown_window_returns_false()
    {
        Assert.False(await Repository().DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_writes_an_audit_entry_for_the_window()
    {
        var doomed = await SeedWindowAsync("Doomed");

        await Repository().DeleteAsync(doomed.WindowId, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        Assert.True(await ctx.AuditEntries.AnyAsync(a =>
            a.EntityType == nameof(CheckingWindow) && a.Action == "Delete" && a.EntityId == doomed.WindowId.ToString()));
    }

    [Fact]
    public async Task Counts_are_scoped_to_the_window()
    {
        var a = await SeedWindowAsync("A");
        var b = await SeedWindowAsync("B");
        await SeedRequestAsync(a.WindowId, a.PupilDataId, RequestStatus.SubmittedUnCommitted);
        await SeedRequestAsync(a.WindowId, a.PupilDataId, RequestStatus.SubmittedUnCommitted);
        await SeedRequestAsync(a.WindowId, a.PupilDataId, RequestStatus.InProgress);
        await SeedRequestAsync(b.WindowId, b.PupilDataId, RequestStatus.SubmittedUnCommitted);
        await SeedEgressRunAsync(a.WindowId);

        var counts = await Repository().CountRequestsByStatusAsync(a.WindowId, CancellationToken.None);

        Assert.Equal(2, counts[RequestStatus.SubmittedUnCommitted]);
        Assert.Equal(1, counts[RequestStatus.InProgress]);
        Assert.Equal(2, counts.Count);
        Assert.Equal(1, await Repository().CountEgressRunsAsync(a.WindowId, CancellationToken.None));
        Assert.Equal(0, await Repository().CountEgressRunsAsync(b.WindowId, CancellationToken.None));
    }

    private WindowDeletionRepository Repository() => new(fixture.CreateContext());

    private sealed record SeededWindow(Guid WindowId, Guid PupilDataId, Guid[] ExerciseIds);

    // Two exercises, the second replacing the first — the RESTRICT pointer that a plain cascade
    // cannot get past — each with a dataset and a published release.
    private async Task<SeededWindow> SeedWindowAsync(string title)
    {
        await using var ctx = fixture.CreateContext();
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified);
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = title,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = start,
            EndDate = end
        };

        var first = Exercise(CheckingExerciseType.PupilData, start, end, replaces: null);
        var second = Exercise(CheckingExerciseType.PupilData, start, end, replaces: first.Id);
        window.CheckingExercises.Add(first);
        window.CheckingExercises.Add(second);

        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return new SeededWindow(window.Id, first.Id, [first.Id, second.Id]);
    }

    private static CheckingExercise Exercise(CheckingExerciseType type, DateTime start, DateTime end, Guid? replaces)
    {
        var id = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        return new CheckingExercise
        {
            Id = id,
            ExerciseType = type,
            TabName = "Pupils",
            StartDate = start,
            EndDate = end,
            ReplacesCheckingExerciseId = replaces,
            CurrentReleaseId = releaseId,
            Datasets = [new CheckingWindowDataset { Id = datasetId, Name = "pupils" }],
            Releases =
            [
                new CheckingExerciseRelease
                {
                    Id = releaseId,
                    Number = 1,
                    PublishedAt = start,
                    Files = [new CheckingExerciseReleaseFile { Id = Guid.NewGuid(), DatasetId = datasetId, DatasetName = "pupils" }]
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

    private async Task SeedEgressRunAsync(Guid windowId)
    {
        await using var ctx = fixture.CreateContext();
        var runId = Guid.NewGuid();
        ctx.EgressRuns.Add(new EgressRun
        {
            Id = runId, WindowId = windowId, Status = EgressRunStatus.TransferFailed,
            StartedById = Guid.NewGuid(), StartedByName = "Ops One", StartedAtUtc = DateTime.UtcNow
        });
        ctx.EgressRunOutputs.Add(new EgressRunOutput
        {
            Id = Guid.NewGuid(), RunId = runId, WindowId = windowId, OutputType = EgressOutputType.RemoveLearners,
            IsActive = false, RawRecordsJson = "[]", SourceRecordCount = 0
        });
        await ctx.SaveChangesAsync();
    }
}
