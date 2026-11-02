using System.Text.Json;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Egress;

// S10: DevEgressControllerTests (unit project) covers only the two 404 cases, which would pass
// even if IsAllowed were hard-coded false — this class adds the positive Development case. Lives
// here against real Postgres for the same reason as DevOutboxEgressTicketSourceTests: Seed and
// Cleanup are built entirely from EF LINQ queries the unit project has no in-memory pattern for.
[Collection(nameof(PostgresCollection))]
public sealed class DevEgressControllerTests(PostgresFixture fixture)
{
    private static readonly Guid WindowId = Guid.Parse("A0000000-0000-0000-0000-00000000E603");
    private static readonly Guid ExerciseId = Guid.Parse("A0000000-0000-0000-0000-00000000E604");

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        if (!await db.CheckingWindows.AnyAsync(w => w.Id == WindowId))
        {
            db.CheckingWindows.Add(new CheckingWindow
            {
                Id = WindowId, Title = "Dev egress window", KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
            });
        }
        if (!await db.CheckingExercises.AnyAsync(e => e.Id == ExerciseId))
        {
            db.CheckingExercises.Add(new CheckingExercise
            {
                Id = ExerciseId, CheckingWindowId = WindowId, ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30), TabOrder = 0
            });
        }
        await db.SaveChangesAsync();
        // DevZendeskTicket has no WindowId; the only link is the request's ReferenceNumber.
        // Remove this window's tickets before the requests go, so the link is still resolvable.
        var references = await db.ChangeRequests.Where(r => r.WindowId == WindowId).Select(r => r.ReferenceNumber).ToListAsync();
        await db.DevZendeskTickets.Where(t => references.Contains(t.ReferenceNumber)).ExecuteDeleteAsync();
        await db.ChangeRequests.Where(r => r.WindowId == WindowId).ExecuteDeleteAsync();
        await db.EgressRuns.Where(r => r.WindowId == WindowId).ExecuteDeleteAsync();
    }

    private DevEgressController Build(IRequestStateBlobClient journeys, IEgressBlobClient blobs) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "true" }).Build(),
            fixture.CreateContext(), journeys, blobs, DevelopmentEnvironment());

    private static IHostEnvironment DevelopmentEnvironment()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        return env;
    }

    // System.Text.Json reads CustomFieldDto.Value (object?) as a JsonElement, whose ToString
    // keeps the JSON quotes. Normalise to the plain text the assertions compare.
    private static string? Text(object? value) => value switch
    {
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText(),
        _ => value?.ToString()
    };

    [Fact]
    public async Task Seed_creates_committed_requests_with_dev_zendesk_tickets_when_allowed()
    {
        await ResetAsync();
        var journeys = Substitute.For<IRequestStateBlobClient>();
        var controller = Build(journeys, Substitute.For<IEgressBlobClient>());

        var result = await controller.Seed(WindowId, "RemoveLearners", "auto_approved", 2, "860/4070", 142313, "pupil-died", null, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await using var db = fixture.CreateContext();
        var requests = await db.ChangeRequests.Where(r => r.WindowId == WindowId).ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal(RequestStatus.Submitted, r.Status));
        Assert.All(requests, r => Assert.NotNull(r.CrmId));
        // #746: stamped with the window's single pupil-data exercise, so the per-exercise
        // decision pull can see the seeded requests.
        Assert.All(requests, r => Assert.Equal(ExerciseId, r.CheckingExerciseId));
        var references = requests.Select(r => r.ReferenceNumber).ToList();
        var tickets = await db.DevZendeskTickets.Where(t => references.Contains(t.ReferenceNumber)).ToListAsync();
        Assert.Equal(2, tickets.Count);
        await journeys.Received(2).SaveAsync(WindowId, Arg.Any<string>(), Arg.Any<RequestState>());
    }

    // #746: a seeded decision reason lands in the ticket's matching reason field, so the dev
    // outbox source can read it back as-is and the whole pull is walkable locally.
    [Fact]
    public async Task Seed_with_a_decision_reason_writes_the_reason_field_with_the_matching_well_known_id()
    {
        await ResetAsync();
        var controller = Build(Substitute.For<IRequestStateBlobClient>(), Substitute.For<IEgressBlobClient>());

        await controller.Seed(WindowId, "RemoveLearners", "auto_rejected", 1, "860/4070", 142313, "pupil-died",
            "Inclusion criteria not met", CancellationToken.None);

        await using var db = fixture.CreateContext();
        var reference = await db.ChangeRequests.Where(r => r.WindowId == WindowId).Select(r => r.ReferenceNumber).SingleAsync();
        var ticket = await db.DevZendeskTickets.Where(t => t.ReferenceNumber == reference).SingleAsync();
        var raw = JsonSerializer.Deserialize<CreateTicketRequestDto>(ticket.RawJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(raw!.Ticket.CustomFields, f => f.Id == DevOutboxEgressTicketSource.WellKnownDecisionFieldId && Text(f.Value) == "auto_rejected");
        Assert.Contains(raw.Ticket.CustomFields, f => f.Id == DevOutboxEgressTicketSource.WellKnownRejectedReasonFieldId && Text(f.Value) == "Inclusion criteria not met");
    }

    [Fact]
    public async Task Seed_without_a_decision_reason_writes_no_reason_field()
    {
        await ResetAsync();
        var controller = Build(Substitute.For<IRequestStateBlobClient>(), Substitute.For<IEgressBlobClient>());

        await controller.Seed(WindowId, "RemoveLearners", "auto_approved", 1, "860/4070", 142313, "pupil-died",
            null, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var reference = await db.ChangeRequests.Where(r => r.WindowId == WindowId).Select(r => r.ReferenceNumber).SingleAsync();
        var ticket = await db.DevZendeskTickets.Where(t => t.ReferenceNumber == reference).SingleAsync();
        var raw = JsonSerializer.Deserialize<CreateTicketRequestDto>(ticket.RawJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(raw!.Ticket.CustomFields, f => f.Id == DevOutboxEgressTicketSource.WellKnownDecisionFieldId && Text(f.Value) == "auto_approved");
        Assert.DoesNotContain(raw.Ticket.CustomFields, f => f.Id == DevOutboxEgressTicketSource.WellKnownApprovedReasonFieldId);
    }

    [Fact]
    public async Task Cleanup_removes_everything_the_seeder_wrote_for_the_window()
    {
        await ResetAsync();
        var journeys = Substitute.For<IRequestStateBlobClient>();
        await Build(journeys, Substitute.For<IEgressBlobClient>())
            .Seed(WindowId, "RemoveLearners", "auto_approved", 1, "860/4070", 142313, "pupil-died", null, CancellationToken.None);

        var result = await Build(journeys, Substitute.For<IEgressBlobClient>()).Cleanup(WindowId, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.ChangeRequests.Where(r => r.WindowId == WindowId).ToListAsync());
        await journeys.Received(1).DeleteAsync(WindowId, Arg.Any<string>());
    }

    // Nit: Cleanup previously deleted blobs by file name alone, which could remove another run's
    // blob if two windows' files happened to collide on name (Q3). It must delete only a blob this
    // run actually owns, via the same egressRunId-metadata check the M1 sweep uses.
    [Fact]
    public async Task Cleanup_deletes_a_blob_using_its_owning_runs_id_not_just_its_file_name()
    {
        await ResetAsync();
        var runId = Guid.NewGuid();
        const string fileName = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv";
        await using (var db = fixture.CreateContext())
        {
            db.EgressRuns.Add(new EgressRun
            {
                Id = runId, WindowId = WindowId, Status = EgressRunStatus.TransferFailed,
                StartedById = Guid.NewGuid(), StartedByName = "Ops One", StartedAtUtc = DateTime.UtcNow
            });
            db.EgressRunOutputs.Add(new EgressRunOutput
            {
                Id = Guid.NewGuid(), RunId = runId, WindowId = WindowId, OutputType = EgressOutputType.RemoveLearners,
                IsActive = false, RawRecordsJson = "[]", SourceRecordCount = 0, FileName = fileName
            });
            await db.SaveChangesAsync();
        }
        var blobs = Substitute.For<IEgressBlobClient>();
        blobs.IsConfigured.Returns(true);
        blobs.DeleteIfOwnedByRunAsync(fileName, runId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Build(Substitute.For<IRequestStateBlobClient>(), blobs).Cleanup(WindowId, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await blobs.Received(1).DeleteIfOwnedByRunAsync(fileName, runId, Arg.Any<CancellationToken>());
    }

    // The blob sweep ran before the row deletes and threw straight out of the action, so on an
    // environment whose egress account was unreachable every cleanup answered 500 and left the
    // runs behind — and a leftover run is what stopped the dev seeder (and so the pod) starting on
    // the next deploy. A dev reset must reset: the database rows go whatever the blob store did.
    [Fact]
    public async Task Cleanup_still_removes_the_runs_when_the_blob_sweep_fails()
    {
        await ResetAsync();
        var runId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            db.EgressRuns.Add(new EgressRun
            {
                Id = runId, WindowId = WindowId, Status = EgressRunStatus.Preprocessed,
                StartedById = Guid.NewGuid(), StartedByName = "Ops One", StartedAtUtc = DateTime.UtcNow
            });
            db.EgressRunOutputs.Add(new EgressRunOutput
            {
                Id = Guid.NewGuid(), RunId = runId, WindowId = WindowId, OutputType = EgressOutputType.RemoveLearners,
                IsActive = true, RawRecordsJson = "[]", SourceRecordCount = 0,
                FileName = "CYPMD_LDS_KS4_RemoveLearners_2026_09_17.csv"
            });
            await db.SaveChangesAsync();
        }
        var blobs = Substitute.For<IEgressBlobClient>();
        blobs.IsConfigured.Returns(true);
        blobs.DeleteIfOwnedByRunAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new HttpRequestException("Connection refused (127.0.0.1:10000)"));

        var result = await Build(Substitute.For<IRequestStateBlobClient>(), blobs).Cleanup(WindowId, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await using var after = fixture.CreateContext();
        Assert.False(await after.EgressRuns.AnyAsync(r => r.Id == runId), "the run survived a cleanup whose blob sweep failed");
    }
}
