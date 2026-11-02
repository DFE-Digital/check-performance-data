using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ExerciseDecisions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.ExerciseDecisions;

// The pull's read is the exercise's submitted rows, across schools and any request type — the
// admin is looking at one exercise, not one school or one journey kind. The save updates the
// rows and writes the deliberate WindowAdmin audit row in one SaveChanges, so the two commit or
// do not commit together; the payload carries ids and counts only, no personal data.
[Collection(nameof(PostgresCollection))]
public sealed class ExerciseDecisionPullRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    // The seeded exercises carry a TabName, and CheckingDataCatalogue reads every visible
    // exercise in every window of the shared database, so the windows must go when the class is
    // done or the catalogue facts fail.
    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await db.ChangeRequests.Where(r => r.WindowId == WindowId || r.WindowId == OtherWindowId).ExecuteDeleteAsync();
        await db.CheckingWindows.Where(w => w.Id == WindowId || w.Id == OtherWindowId).ExecuteDeleteAsync();
    }

    private static readonly Guid WindowId = Guid.Parse("A0000000-0000-0000-0000-00000000E702");
    private static readonly Guid OtherWindowId = Guid.Parse("A0000000-0000-0000-0000-00000000E703");
    private static readonly Guid ExerciseId = Guid.Parse("A0000000-0000-0000-0000-00000000E712");
    private static readonly Guid OtherExerciseId = Guid.Parse("A0000000-0000-0000-0000-00000000E713");
    private static readonly Guid UserId = Guid.Parse("A0000000-0000-0000-0000-00000000E7AA");

    private ExerciseDecisionPullRepository Repository() => new(fixture.CreateContext());

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        foreach (var (id, title) in new[] { (WindowId, "Decision pull window"), (OtherWindowId, "Decision pull window two") })
        {
            if (!await db.CheckingWindows.AnyAsync(w => w.Id == id))
            {
                db.CheckingWindows.Add(new CheckingWindow
                {
                    Id = id, Title = title, KeyStage = KeyStages.KS4,
                    CheckingWindowType = CheckingWindowType.KS4June,
                    StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
                });
            }
        }
        if (!await db.CheckingExercises.AnyAsync(e => e.Id == ExerciseId || e.Id == OtherExerciseId))
        {
            db.CheckingExercises.AddRange(
                new CheckingExercise
                {
                    Id = ExerciseId, CheckingWindowId = WindowId, ExerciseType = CheckingExerciseType.PupilData,
                    TabName = "Pupils", TabOrder = 1, IsEnabled = true,
                    StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
                },
                new CheckingExercise
                {
                    Id = OtherExerciseId, CheckingWindowId = WindowId, ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    TabName = "Results", TabOrder = 2, IsEnabled = true,
                    StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
                });
        }
        await db.SaveChangesAsync();
        await db.ChangeRequests.Where(r => r.WindowId == WindowId || r.WindowId == OtherWindowId).ExecuteDeleteAsync();
        // The audit log is delete-protected by trigger (AuditEntryActionLengthTests); TRUNCATE is
        // the established test reset, as in RulesConfigVersionRepositoryTests.
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE \"AuditEntries\" RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static ChangeRequest Row(
        Guid windowId, Guid? exerciseId, string reference, RequestStatus status, string? crm,
        ZendeskDecision? decision = null, string? reason = null) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = windowId,
        CheckingExerciseId = exerciseId,
        OrganisationUrn = 142313,
        OrganisationLaestab = "860/4070",
        Submitted = new DateTime(2026, 6, 5, 9, 0, 0),
        SubmittedById = UserId,
        SubmittedByName = "School User",
        Status = status,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove a pupil",
        AmendmentType = WhatToChange.Remove,
        CrmId = crm,
        ZendeskDecision = decision,
        ZendeskDecisionReason = reason
    };

    [Fact]
    public async Task GetSubmittedRowsAsync_returns_only_the_exercises_submitted_rows()
    {
        await ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            db.ChangeRequests.AddRange(
                Row(WindowId, ExerciseId, "P-1", RequestStatus.Submitted, "1001"),
                Row(WindowId, ExerciseId, "P-2", RequestStatus.Submitted, null),
                Row(WindowId, ExerciseId, "P-3", RequestStatus.Withdrawn, "1003"),
                Row(WindowId, ExerciseId, "P-4", RequestStatus.InProgress, "1004"),
                Row(WindowId, OtherExerciseId, "E-1", RequestStatus.Submitted, "2001"),
                Row(WindowId, null, "N-1", RequestStatus.Submitted, "3001"),
                Row(OtherWindowId, ExerciseId, "W-1", RequestStatus.Submitted, "4001"));
            await db.SaveChangesAsync();
        }

        var rows = await Repository().GetSubmittedRowsAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(["P-1", "P-2"], rows.Select(r => r.ReferenceNumber).Order());
        Assert.Equal("1001", rows.Single(r => r.ReferenceNumber == "P-1").CrmId);
        Assert.Null(rows.Single(r => r.ReferenceNumber == "P-2").CrmId);
    }

    [Fact]
    public async Task GetSubmittedRowsAsync_carries_the_stored_decision_when_there_is_one()
    {
        await ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            db.ChangeRequests.AddRange(
                Row(WindowId, ExerciseId, "P-1", RequestStatus.Submitted, "1001", ZendeskDecision.Accepted, "Inclusion criteria met"),
                Row(WindowId, ExerciseId, "P-2", RequestStatus.Submitted, "1002"));
            await db.SaveChangesAsync();
        }

        var rows = await Repository().GetSubmittedRowsAsync(WindowId, ExerciseId, CancellationToken.None);

        var stored = rows.Single(r => r.ReferenceNumber == "P-1");
        Assert.Equal(ZendeskDecision.Accepted, stored.ZendeskDecision);
        Assert.Equal("Inclusion criteria met", stored.ZendeskDecisionReason);
        Assert.Null(rows.Single(r => r.ReferenceNumber == "P-2").ZendeskDecision);
        Assert.Null(rows.Single(r => r.ReferenceNumber == "P-2").ZendeskDecisionReason);
    }

    [Fact]
    public async Task SaveDecisionsAsync_updates_the_rows_and_writes_the_deliberate_audit_row()
    {
        await ResetAsync();
        var acceptedId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            var p1 = Row(WindowId, ExerciseId, "P-1", RequestStatus.Submitted, "1001");
            var p2 = Row(WindowId, ExerciseId, "P-2", RequestStatus.Submitted, "1002");
            acceptedId = p1.Id;
            rejectedId = p2.Id;
            db.ChangeRequests.AddRange(p1, p2);
            await db.SaveChangesAsync();
        }

        var saved = new List<SavedDecision>
        {
            new(acceptedId, ZendeskDecision.Accepted, "Inclusion criteria met"),
            new(rejectedId, ZendeskDecision.Rejected, null)
        };
        var audit = new DecisionsPulledAudit
        {
            WindowId = WindowId,
            ExerciseId = ExerciseId,
            Accepted = 1,
            Rejected = 1,
            Unchanged = 1,
            NoTicket = 1,
            TicketNotFound = 0,
            RanAtUtc = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc),
            UserId = UserId.ToString()
        };

        var updated = await Repository().SaveDecisionsAsync(saved, audit, CancellationToken.None);

        Assert.Equal(2, updated);
        await using var check = fixture.CreateContext();
        var savedP1 = await check.ChangeRequests.AsNoTracking().SingleAsync(r => r.Id == acceptedId);
        var savedP2 = await check.ChangeRequests.AsNoTracking().SingleAsync(r => r.Id == rejectedId);
        Assert.Equal(ZendeskDecision.Accepted, savedP1.ZendeskDecision);
        Assert.Equal("Inclusion criteria met", savedP1.ZendeskDecisionReason);
        Assert.Equal(ZendeskDecision.Rejected, savedP2.ZendeskDecision);
        Assert.Null(savedP2.ZendeskDecisionReason);

        var entry = await check.AuditEntries.AsNoTracking().SingleAsync(a =>
            a.EntityType == AuditActivities.WindowAdmin && a.Action == AuditActivities.DecisionsPulledAction);
        Assert.Equal(WindowId.ToString(), entry.EntityId);
        Assert.Equal(UserId.ToString(), entry.UserId);
        Assert.Equal(audit.RanAtUtc, entry.Timestamp);
        var payload = JsonSerializer.Deserialize<JsonElement>(entry.NewValues!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Decision pull window", payload.GetProperty("windowTitle").GetString());
        Assert.Equal(1, payload.GetProperty("accepted").GetInt32());
        Assert.Equal(1, payload.GetProperty("rejected").GetInt32());
        Assert.Equal(1, payload.GetProperty("unchanged").GetInt32());
        Assert.Equal(1, payload.GetProperty("noTicket").GetInt32());
        Assert.Equal(0, payload.GetProperty("ticketNotFound").GetInt32());
    }

    [Fact]
    public async Task SaveDecisionsAsync_leaves_rows_without_a_saved_decision_untouched()
    {
        await ResetAsync();
        var savedId = Guid.NewGuid();
        var untouchedId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            var saved = Row(WindowId, ExerciseId, "P-1", RequestStatus.Submitted, "1001", ZendeskDecision.Accepted, "An earlier reason");
            var untouched = Row(WindowId, ExerciseId, "P-2", RequestStatus.Submitted, "1002");
            savedId = saved.Id;
            untouchedId = untouched.Id;
            db.ChangeRequests.AddRange(saved, untouched);
            await db.SaveChangesAsync();
        }

        await Repository().SaveDecisionsAsync(
            [new SavedDecision(savedId, ZendeskDecision.Rejected, "Inclusion criteria not met")],
            new DecisionsPulledAudit
            {
                WindowId = WindowId, ExerciseId = ExerciseId,
                Accepted = 0, Rejected = 1, Unchanged = 1,
                NoTicket = 0, TicketNotFound = 0, RanAtUtc = DateTime.UtcNow, UserId = UserId.ToString()
            },
            CancellationToken.None);

        await using var check = fixture.CreateContext();
        var untouchedRow = await check.ChangeRequests.AsNoTracking().SingleAsync(r => r.Id == untouchedId);
        Assert.Null(untouchedRow.ZendeskDecision);
        Assert.Null(untouchedRow.ZendeskDecisionReason);
    }
}
