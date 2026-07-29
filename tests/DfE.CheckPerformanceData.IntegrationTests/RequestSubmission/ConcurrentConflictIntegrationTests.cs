using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

[Collection(nameof(PostgresCollection))]
public sealed class ConcurrentConflictIntegrationTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Upsert_ConcurrentUpdate_ForSamePupil_OneSucceedsOneThrows()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONCUR-A", RequestStatus.InProgress, pupilId: pupilId, submittedById: userA));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONCUR-B", RequestStatus.InProgress, pupilId: pupilId, submittedById: userB));

        var results = new System.Collections.Concurrent.ConcurrentBag<(string Ref, Guid? Id, Exception? Ex)>();

        var taskA = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            var repo = new RequestRepository(ctx);
            try
            {
                var id = await repo.UpsertAsync(Data(windowId, "REF-CONCUR-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userA));
                results.Add(("REF-CONCUR-A", id, null));
            }
            catch (Exception ex)
            {
                results.Add(("REF-CONCUR-A", null, ex));
            }
        });

        var taskB = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            var repo = new RequestRepository(ctx);
            try
            {
                var id = await repo.UpsertAsync(Data(windowId, "REF-CONCUR-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userB));
                results.Add(("REF-CONCUR-B", id, null));
            }
            catch (Exception ex)
            {
                results.Add(("REF-CONCUR-B", null, ex));
            }
        });

        await Task.WhenAll(taskA, taskB);

        Assert.Equal(2, results.Count);
        var success = results.Where(r => r.Ex is null).ToList();
        var failures = results.Where(r => r.Ex is not null).ToList();
        Assert.Single(success);
        Assert.Single(failures);
        Assert.IsType<DuplicateRequestException>(failures[0].Ex);

        await using var verify = _fixture.CreateContext();
        var rows = await verify.ChangeRequests
            .Where(r => r.PupilId == pupilId && r.Status == RequestStatus.SubmittedUnCommitted)
            .ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Upsert_SaveDraftThenSubmit_DetectsConflictWithConcurrentSubmission()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-DRAFT-A", RequestStatus.InProgress, pupilId: pupilId, submittedById: userA));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-SUBMIT-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userB));

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            new RequestRepository(_fixture.CreateContext())
                .UpsertAsync(Data(windowId, "REF-DRAFT-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userA)));

        Assert.Equal(ConflictType.OtherSubmitted, ex.ConflictType);
        Assert.Equal("Test User", ex.ConflictingUserName);
    }

    [Fact]
    public async Task Upsert_SingleUserDraftSave_DoesNotTriggerFalsePositive()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var draftId = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-SELF-DRAFT", RequestStatus.InProgress, pupilId: pupilId, submittedById: userId));

        var submitId = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-SELF-DRAFT", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userId));

        Assert.Equal(draftId, submitId);

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "REF-SELF-DRAFT");
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);
    }

    [Fact]
    public async Task Upsert_Insert_ConcurrentForSamePupil_OneSucceedsOneThrows()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var results = new System.Collections.Concurrent.ConcurrentBag<(string Ref, Guid? Id, Exception? Ex)>();

        var taskA = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            var repo = new RequestRepository(ctx);
            try
            {
                var id = await repo.UpsertAsync(Data(windowId, "REF-INS-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userA));
                results.Add(("REF-INS-A", id, null));
            }
            catch (Exception ex)
            {
                results.Add(("REF-INS-A", null, ex));
            }
        });

        var taskB = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            var repo = new RequestRepository(ctx);
            try
            {
                var id = await repo.UpsertAsync(Data(windowId, "REF-INS-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilId, submittedById: userB));
                results.Add(("REF-INS-B", id, null));
            }
            catch (Exception ex)
            {
                results.Add(("REF-INS-B", null, ex));
            }
        });

        await Task.WhenAll(taskA, taskB);

        Assert.Equal(2, results.Count);
        var success = results.Where(r => r.Ex is null).ToList();
        var failures = results.Where(r => r.Ex is not null).ToList();
        Assert.Single(success);
        Assert.Single(failures);
        Assert.IsType<DuplicateRequestException>(failures[0].Ex);
    }

    [Fact]
    public async Task Upsert_BulkSubmissionMixedConflicts_ProcessesNonConflictingPupils()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilA = Guid.NewGuid();
        var pupilB = Guid.NewGuid();
        var pupilC = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-A", RequestStatus.ReadyToSubmit, pupilId: pupilA, submittedById: userA));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-B", RequestStatus.ReadyToSubmit, pupilId: pupilB, submittedById: userA));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-C", RequestStatus.ReadyToSubmit, pupilId: pupilC, submittedById: userA));

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONFLICT-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilB, submittedById: userB));

        var submittedPupilIds = await new RequestRepository(_fixture.CreateContext())
            .GetSubmittedPupilIdsAsync(windowId, 100000);

        Assert.Contains(pupilB, submittedPupilIds);
        Assert.DoesNotContain(pupilA, submittedPupilIds);
        Assert.DoesNotContain(pupilC, submittedPupilIds);

        var resultA = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilA, submittedById: userA));

        var exB = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            new RequestRepository(_fixture.CreateContext())
                .UpsertAsync(Data(windowId, "REF-BULK-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilB, submittedById: userA)));

        var resultC = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-C", RequestStatus.SubmittedUnCommitted, pupilId: pupilC, submittedById: userA));

        Assert.NotEqual(Guid.Empty, resultA);
        Assert.Equal(ConflictType.OtherSubmitted, exB.ConflictType);
        Assert.NotEqual(Guid.Empty, resultC);

        await using var ctx = _fixture.CreateContext();
        var submitted = await ctx.ChangeRequests
            .Where(r => r.Status == RequestStatus.SubmittedUnCommitted && r.WindowId == windowId)
            .ToListAsync();
        Assert.Equal(3, submitted.Count);
    }

    [Fact]
    public async Task Upsert_BulkSubmission_FirstPupilConflict_DoesNotBlockRemaining()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilA = Guid.NewGuid();
        var pupilB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-A", RequestStatus.ReadyToSubmit, pupilId: pupilA, submittedById: userA));
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-B", RequestStatus.ReadyToSubmit, pupilId: pupilB, submittedById: userA));

        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-CONFLICT-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilA, submittedById: userB));

        var exA = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            new RequestRepository(_fixture.CreateContext())
                .UpsertAsync(Data(windowId, "REF-BULK-A", RequestStatus.SubmittedUnCommitted, pupilId: pupilA, submittedById: userA)));

        var resultB = await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(Data(windowId, "REF-BULK-B", RequestStatus.SubmittedUnCommitted, pupilId: pupilB, submittedById: userA));

        Assert.Equal(ConflictType.OtherSubmitted, exA.ConflictType);
        Assert.NotEqual(Guid.Empty, resultB);
    }

    private static ChangeRequestData Data(
        Guid windowId, string referenceNumber, RequestStatus status = RequestStatus.SubmittedUnCommitted,
        long organisationUrn = 100000, Guid? pupilId = null, string? pupilUpn = "UPN1", Guid? submittedById = null) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = organisationUrn,
            PupilId = pupilId,
            PupilUpn = pupilUpn,
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = submittedById ?? Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = status,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove"
        };

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedWindowAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
