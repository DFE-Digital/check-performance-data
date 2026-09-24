using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;

namespace DfE.CheckPerformanceData.IntegrationTests.Audit;

// AB#294592. Audit rows can never be deleted (the immutability trigger), and the context's own
// capture writes rows for every entity these tests create, so nothing here asserts on the whole
// table: every fact isolates by a fresh window id or a per-test synthetic entity type, and the
// egress rows are written through the real EgressRunRepository so the payloads are production's.
// Note the capture also records each run's creation as EgressRun / Insert (the pull) — so a window
// with two runs holds five rows: two Inserts, one Transfer, one TransferFailed, one CheckingWindow.
[Collection(nameof(PostgresCollection))]
public sealed class AuditLogRepositoryTests(PostgresFixture fixture)
{
    private static readonly Guid UserId = Guid.Parse("A0000000-0000-0000-0000-00000000A5AA");

    private AuditLogRepository Repository() => new(fixture.CreateContext());

    private async Task<Guid> NewWindowAsync(string title)
    {
        await using var db = fixture.CreateContext();
        var id = Guid.NewGuid();
        db.CheckingWindows.Add(new CheckingWindow
        {
            Id = id, Title = title, KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
        });
        await db.SaveChangesAsync();   // the context's capture writes CheckingWindow / Insert with EntityId = id
        return id;
    }

    private static EgressSourceRecord Record() => new()
    {
        ChangeRequestId = Guid.NewGuid(), ReferenceNumber = "REF-1", TicketId = 1001, Decision = "auto_approved",
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313,
        OrganisationLaestab = "860/4070", PupilSurname = "Smith", PupilFirstname = "Alice", JourneyFound = true
    };

    private static EgressRunCreate Create(Guid windowId, params EgressOutputType[] types) => new(
        windowId, UserId, "Ops One", "ops.one@education.gov.uk",
        types.Select(t => new EgressRunOutputCreate(t, [Record()])).ToList());

    private static EgressTransferAudit Audit(params EgressOutputType[] types) => new(UserId.ToString(), "Ops One", "cypmd/extracts_input",
        types.ToDictionary(t => t, t => ($"CYPMD_LDS_KS4_{t}_2026_06_08.csv", 2, "ABC")));

    // One failed and one transferred run in a fresh window. The failed row's Timestamp is "now";
    // the transferred row's is the transfer instant passed in (8 Jun 2026), so failed lists first.
    private async Task<(Guid WindowId, Guid Transferred, Guid Failed)> SeedEgressAsync()
    {
        var windowId = await NewWindowAsync("Audit log window");
        var egress = new EgressRunRepository(fixture.CreateContext());
        var failed = await egress.CreateRunAsync(Create(windowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await egress.MarkTransferFailedAsync(failed, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);
        var transferred = await egress.CreateRunAsync(Create(windowId, EgressOutputType.RemoveLearners, EgressOutputType.NewLearners), CancellationToken.None);
        await egress.MarkTransferredAsync(transferred, EgressRunStatus.Pulled, Audit(EgressOutputType.RemoveLearners, EgressOutputType.NewLearners),
            new DateTime(2026, 6, 8, 14, 38, 0, DateTimeKind.Utc), CancellationToken.None);
        return (windowId, transferred, failed);
    }

    private static string NewProbeType() => $"AuditLogProbe{Guid.NewGuid():N}";

    private async Task SeedProbeRowsAsync(string probe)
    {
        await using var db = fixture.CreateContext();
        var at = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        db.AuditEntries.Add(new AuditEntry { EntityType = probe, EntityId = "older", Action = "Insert", Timestamp = at.AddMinutes(-1), UserId = "probe-user" });
        db.AuditEntries.Add(new AuditEntry { EntityType = probe, EntityId = "first", Action = "Update", Timestamp = at, UserId = "probe-user" });
        db.AuditEntries.Add(new AuditEntry { EntityType = probe, EntityId = "second", Action = "Update", Timestamp = at, UserId = "probe-user" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_window_filter_returns_that_windows_egress_rows_and_its_own_row_newest_first()
    {
        var (windowId, transferred, failed) = await SeedEgressAsync();

        var page = await Repository().ListAsync(new AuditLogFilter(null, windowId, null), 1, 20, CancellationToken.None);

        Assert.Equal(page.Rows.Count, page.TotalCount);
        Assert.All(page.Rows, r => Assert.True(
            (r.EntityType == AuditActivities.Egress && (r.EntityId == transferred.ToString() || r.EntityId == failed.ToString())) ||
            (r.EntityType == AuditActivities.CheckingWindow && r.EntityId == windowId.ToString()),
            $"unexpected row {r.EntityType}/{r.EntityId}"));
        Assert.Contains(page.Rows, r => r.EntityType == AuditActivities.Egress && r.EntityId == transferred.ToString());
        Assert.Contains(page.Rows, r => r.EntityType == AuditActivities.Egress && r.EntityId == failed.ToString());
        Assert.Contains(page.Rows, r => r.EntityType == AuditActivities.CheckingWindow);
        Assert.Equal(5, page.Rows.Count);   // two Inserts (the pulls), TransferFailed, Transfer, the window's own Insert
        Assert.Equal(page.Rows.Select(r => r.TimestampUtc).OrderByDescending(t => t), page.Rows.Select(r => r.TimestampUtc));
        // The TransferFailed row is stamped "now"; the Transfer row carries the June 2026 instant
        // passed to MarkTransferredAsync, so it lists last — newest first is by Timestamp, not by insertion.
        var rows = page.Rows.ToList();
        Assert.True(rows.FindIndex(r => r.EntityId == failed.ToString() && r.Action == AuditActivities.TransferFailedAction)
                    < rows.FindIndex(r => r.EntityId == transferred.ToString() && r.Action == AuditActivities.TransferAction));
        Assert.Equal(AuditActivities.TransferAction, rows[^1].Action);
    }

    [Fact]
    public async Task Egress_rows_are_decorated_from_their_payload_and_the_window_row_is_not()
    {
        var (windowId, transferred, failed) = await SeedEgressAsync();

        var rows = (await Repository().ListAsync(new AuditLogFilter(null, windowId, null), 1, 20, CancellationToken.None)).Rows;

        var success = rows.Single(r => r.EntityId == transferred.ToString() && r.Action == AuditActivities.TransferAction);
        Assert.Equal(AuditOutcome.Success, success.Outcome);
        Assert.Equal("Ops One", success.UserName);
        Assert.Equal(UserId.ToString(), success.UserId);
        Assert.Equal(windowId, success.WindowId);
        Assert.Equal("Audit log window", success.WindowTitle);
        Assert.Equal(new[] { "NewLearners", "RemoveLearners" }, success.OutputTypes);
        Assert.Equal(DateTimeKind.Utc, success.TimestampUtc.Kind);

        var failure = rows.Single(r => r.EntityId == failed.ToString() && r.Action == AuditActivities.TransferFailedAction);
        Assert.Equal(AuditOutcome.Failed, failure.Outcome);
        Assert.Equal("Ops One", failure.UserName);
        Assert.Equal("Audit log window", failure.WindowTitle);
        Assert.Equal(new[] { "RemoveLearners" }, failure.OutputTypes);

        // The pulls: the generic capture's PascalCase payload still yields the window and the person
        // who started the run; no outcome and no output types.
        var pulled = rows.Where(r => r.EntityType == AuditActivities.Egress && r.Action == "Insert").ToList();
        Assert.Equal(2, pulled.Count);
        Assert.All(pulled, r =>
        {
            Assert.Null(r.Outcome);
            Assert.Equal(windowId, r.WindowId);
            Assert.Equal("Audit log window", r.WindowTitle);
            Assert.Empty(r.OutputTypes);
            Assert.Equal("Ops One", r.UserName);   // StartedByName from the run row's payload; UserId is the capture's current user
        });

        var window = rows.First(r => r.EntityType == AuditActivities.CheckingWindow);
        Assert.Null(window.Outcome);
        Assert.Null(window.UserName);
        Assert.Equal(windowId, window.WindowId);
        Assert.Equal("Audit log window", window.WindowTitle);
        Assert.Empty(window.OutputTypes);
        Assert.Equal("Insert", window.Action);
    }

    [Fact]
    public async Task The_status_filter_is_egress_only_and_cumulative_with_window_and_activity()
    {
        var (windowId, transferred, failed) = await SeedEgressAsync();
        var repo = Repository();

        var failedOnly = await repo.ListAsync(new AuditLogFilter(null, windowId, AuditOutcome.Failed), 1, 20, CancellationToken.None);
        Assert.Equal(failed.ToString(), Assert.Single(failedOnly.Rows).EntityId);

        var successOnly = await repo.ListAsync(new AuditLogFilter(null, windowId, AuditOutcome.Success), 1, 20, CancellationToken.None);
        Assert.Equal(transferred.ToString(), Assert.Single(successOnly.Rows).EntityId);

        // A status on a non-egress activity can match nothing: the window's own row has no outcome.
        var none = await repo.ListAsync(new AuditLogFilter(AuditActivities.CheckingWindow, windowId, AuditOutcome.Success), 1, 20, CancellationToken.None);
        Assert.Empty(none.Rows);
        Assert.Equal(0, none.TotalCount);
        Assert.Equal(1, none.Page);
        Assert.Equal(1, none.TotalPages);

        var windowRowOnly = await repo.ListAsync(new AuditLogFilter(AuditActivities.CheckingWindow, windowId, null), 1, 20, CancellationToken.None);
        Assert.All(windowRowOnly.Rows, r => Assert.Equal(AuditActivities.CheckingWindow, r.EntityType));
        Assert.NotEmpty(windowRowOnly.Rows);
    }

    [Fact]
    public async Task The_status_filter_never_matches_a_non_egress_row_that_shares_the_action_name()
    {
        // Guards the EntityType half of the status clause: another entity type may legitimately
        // record a "Transfer" or "TransferFailed" action, and it must neither gain an outcome nor
        // answer a status filter. Every row of the window facts is either egress or a CheckingWindow
        // Insert, so without this probe the clause could be dropped unnoticed.
        var probe = NewProbeType();
        await using (var db = fixture.CreateContext())
        {
            var at = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            db.AuditEntries.Add(new AuditEntry { EntityType = probe, EntityId = "t", Action = AuditActivities.TransferAction, Timestamp = at, UserId = "probe-user" });
            db.AuditEntries.Add(new AuditEntry { EntityType = probe, EntityId = "f", Action = AuditActivities.TransferFailedAction, Timestamp = at, UserId = "probe-user" });
            await db.SaveChangesAsync();
        }
        var repo = Repository();

        var unfiltered = await repo.ListAsync(new AuditLogFilter(probe, null, null), 1, 20, CancellationToken.None);
        Assert.Equal(2, unfiltered.TotalCount);
        Assert.All(unfiltered.Rows, r => Assert.Null(r.Outcome));

        var success = await repo.ListAsync(new AuditLogFilter(probe, null, AuditOutcome.Success), 1, 20, CancellationToken.None);
        Assert.Empty(success.Rows);
        Assert.Equal(0, success.TotalCount);

        var failed = await repo.ListAsync(new AuditLogFilter(probe, null, AuditOutcome.Failed), 1, 20, CancellationToken.None);
        Assert.Empty(failed.Rows);
        Assert.Equal(0, failed.TotalCount);
    }

    [Fact]
    public async Task The_activity_filter_isolates_one_entity_type_and_ties_break_on_id()
    {
        var probe = NewProbeType();
        await SeedProbeRowsAsync(probe);

        var page = await Repository().ListAsync(new AuditLogFilter(probe, null, null), 1, 20, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { "second", "first", "older" }, page.Rows.Select(r => r.EntityId));
        Assert.All(page.Rows, r =>
        {
            Assert.Equal(probe, r.EntityType);
            Assert.Equal("probe-user", r.UserId);
            Assert.Null(r.UserName);
            Assert.Null(r.Outcome);
            Assert.Null(r.WindowId);
            Assert.Null(r.WindowTitle);
            Assert.Empty(r.OutputTypes);
        });
        Assert.Equal("Update", page.Rows[0].Action);
    }

    [Fact]
    public async Task An_out_of_range_page_clamps_and_a_page_size_below_one_is_one()
    {
        var probe = NewProbeType();
        await SeedProbeRowsAsync(probe);
        var repo = Repository();

        var last = await repo.ListAsync(new AuditLogFilter(probe, null, null), 9, 2, CancellationToken.None);
        Assert.Equal(2, last.Page);
        Assert.Equal(2, last.TotalPages);
        Assert.Equal("older", Assert.Single(last.Rows).EntityId);

        var first = await repo.ListAsync(new AuditLogFilter(probe, null, null), 0, 2, CancellationToken.None);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.Rows.Count);

        var one = await repo.ListAsync(new AuditLogFilter(probe, null, null), 1, 0, CancellationToken.None);
        Assert.Equal(1, one.PageSize);
        Assert.Equal(3, one.TotalPages);
    }

    [Fact]
    public async Task Stream_yields_the_same_rows_in_the_same_order_as_paging()
    {
        var (windowId, _, _) = await SeedEgressAsync();
        var repo = Repository();
        var filter = new AuditLogFilter(null, windowId, null);

        var paged = (await repo.ListAsync(filter, 1, 100, CancellationToken.None)).Rows;
        var streamed = new List<AuditLogRow>();
        await foreach (var row in repo.StreamAsync(filter, CancellationToken.None))
            streamed.Add(row);

        Assert.Equal(paged.Select(r => r.Id), streamed.Select(r => r.Id));
        Assert.Equal(paged.Select(r => r.WindowTitle), streamed.Select(r => r.WindowTitle));
        Assert.Equal(paged.Select(r => r.UserName), streamed.Select(r => r.UserName));
    }

    [Fact]
    public async Task Activities_are_distinct_and_always_include_data_egress()
    {
        var probe = NewProbeType();
        await SeedProbeRowsAsync(probe);

        var activities = await Repository().ListActivitiesAsync(CancellationToken.None);

        Assert.Contains(AuditActivities.Egress, activities);
        Assert.Contains(probe, activities);
        Assert.Equal(activities.Count, activities.Distinct().Count());
    }

    [Fact]
    public void A_row_never_carries_a_payload()
    {
        // The generic capture stores pupil-bearing entities' values in OldValues/NewValues; the
        // audit log must not be able to render them even by accident.
        Assert.DoesNotContain(typeof(AuditLogRow).GetProperties(), p => p.Name.Contains("Values", StringComparison.Ordinal));
    }
}
