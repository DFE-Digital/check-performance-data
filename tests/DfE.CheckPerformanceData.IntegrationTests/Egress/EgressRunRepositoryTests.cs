using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Egress;

[Collection(nameof(PostgresCollection))]
public sealed class EgressRunRepositoryTests(PostgresFixture fixture)
{
    private static readonly Guid WindowId = Guid.Parse("A0000000-0000-0000-0000-00000000E602");
    private static readonly Guid WindowId2 = Guid.Parse("A0000000-0000-0000-0000-00000000E603");
    private static readonly Guid UserId = Guid.Parse("A0000000-0000-0000-0000-00000000E6AA");

    private EgressRunRepository Repository() => new(fixture.CreateContext());

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        foreach (var (id, title) in new[] { (WindowId, "Egress repo window"), (WindowId2, "Egress repo window two") })
        {
            if (!await db.CheckingWindows.AnyAsync(w => w.Id == id))
            {
                db.CheckingWindows.Add(new CheckingWindow
                {
                    Id = id, Title = title, KeyStage = KeyStages.KS4,
                    CheckingWindowType = CheckingWindowType.KS4June,
                    StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
                });
                await db.SaveChangesAsync();
            }
        }
        await db.EgressRuns.Where(r => r.WindowId == WindowId || r.WindowId == WindowId2).ExecuteDeleteAsync();
        await db.ChangeRequests.Where(r => r.WindowId == WindowId || r.WindowId == WindowId2).ExecuteDeleteAsync();
    }

    private static EgressSourceRecord Record(string reference, long? ticket, string decision) => new()
    {
        ChangeRequestId = Guid.NewGuid(), ReferenceNumber = reference, TicketId = ticket, Decision = decision,
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313,
        OrganisationLaestab = "860/4070", PupilSurname = "Smith", PupilFirstname = "Alice", JourneyFound = true
    };

    private static EgressRunCreate Create(params EgressOutputType[] types) => new(
        WindowId, UserId, "Ops One", "ops.one@education.gov.uk",
        types.Select(t => new EgressRunOutputCreate(t, [Record("REF-1", 1001, "auto_approved"), Record("REF-2", 1002, "rejected")])).ToList());

    private static EgressRunCreate CreateIn(Guid windowId, params EgressOutputType[] types) => new(
        windowId, UserId, "Ops One", "ops.one@education.gov.uk",
        types.Select(t => new EgressRunOutputCreate(t, [Record("REF-1", 1001, "auto_approved"), Record("REF-2", 1002, "rejected")])).ToList());

    private static RemoveLearnerRow RemoveRow(string reference = "REF-1") =>
        new("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, reference);

    private static readonly IReadOnlyDictionary<EgressOutputType, string> RemoveFileName = new Dictionary<EgressOutputType, string>
    {
        [EgressOutputType.RemoveLearners] = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv"
    };

    private static EgressTransferAudit Audit(int records) => new(UserId.ToString(), "Ops One", "cypmd/extracts_input",
        new Dictionary<EgressOutputType, (string, int, string)> { [EgressOutputType.RemoveLearners] = ("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", records, "ABC") });

    private static EgressRunHistoryFilter In(Guid windowId, EgressRunOutcome? outcome = null) => new(windowId, outcome);

    [Fact]
    public async Task Create_then_get_round_trips_the_raw_records()
    {
        await ResetAsync();
        var repo = Repository();

        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var run = await repo.GetRunAsync(id, CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(EgressRunStatus.Pulled, run!.Status);
        var output = Assert.Single(run.Outputs);
        Assert.True(output.IsActive);
        Assert.Equal(2, output.SourceRecordCount);
        Assert.Equal(["REF-1", "REF-2"], output.Records.Select(r => r.ReferenceNumber));
        Assert.Equal("auto_approved", output.Records[0].Decision);
    }

    [Fact]
    public async Task A_second_run_for_the_same_pair_is_refused_and_names_the_blocker()
    {
        await ResetAsync();
        var repo = Repository();
        var first = await repo.CreateRunAsync(Create(EgressOutputType.NewLearners), CancellationToken.None);

        var blocker = await repo.FindBlockerAsync(WindowId, EgressOutputType.NewLearners, CancellationToken.None);
        Assert.NotNull(blocker);
        Assert.Equal(first, blocker!.RunId);
        Assert.Equal("Ops One", blocker.StartedByName);
        Assert.Equal(EgressRunStatus.Pulled, blocker.Status);

        await Assert.ThrowsAsync<EgressRunConflictException>(() =>
            Repository().CreateRunAsync(Create(EgressOutputType.NewLearners), CancellationToken.None));
        Assert.Null(await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None));
    }

    [Fact]
    public async Task Preprocessing_failure_releases_the_pair_and_keeps_the_failures()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);

        await repo.MarkPreprocessingFailedAsync(id, EgressRunStatus.Pulled,
            [new EgressRecordFailure("Split DfE establishment number", 1001, "REF-1", "Local_Authority", "must be 3 digits")],
            CancellationToken.None);

        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.PreprocessingFailed, run!.Status);
        Assert.Single(run.Failures);
        Assert.Equal("Local_Authority", run.Failures[0].Field);
        Assert.Null(await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None));
    }

    // M2: the repository side of "no re-run" — a failed run's own outputs stay inactive forever
    // (EgressPreprocessor now refuses to touch them again), and a fresh run for the same pair is
    // admitted rather than blocked, exactly as the Failed page's "start a new run" copy promises.
    [Fact]
    public async Task A_fresh_run_for_the_same_pair_is_admitted_after_a_preprocessing_failure()
    {
        await ResetAsync();
        var repo = Repository();
        var failed = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.MarkPreprocessingFailedAsync(failed, EgressRunStatus.Pulled,
            [new EgressRecordFailure("Split DfE establishment number", 1001, "REF-1", "Local_Authority", "must be 3 digits")],
            CancellationToken.None);

        var retry = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);

        var failedRun = await repo.GetRunAsync(failed, CancellationToken.None);
        Assert.All(failedRun!.Outputs, o => Assert.False(o.IsActive));
        var blocker = await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None);
        Assert.Equal(retry, blocker!.RunId);
    }

    // S2: EnableRetryOnFailure re-runs the whole execution-strategy delegate on a transient fault.
    // A prior attempt's AddRange calls leave their (never-persisted) entities tracked as Added; if
    // the retry's AddRange runs again without clearing the tracker first, both sets of entities get
    // saved — 2N learner rows for an N-row preprocessing run, which is what LDS receives as the
    // file. Reproduced here by tracking a stale entity by hand rather than forcing a real transient
    // Postgres fault, which the same ChangeTracker.Clear() at the top of the delegate must discard.
    [Fact]
    public async Task A_stale_tracked_entity_left_by_an_earlier_attempt_is_not_saved_alongside_the_real_one()
    {
        await ResetAsync();
        var context = fixture.CreateContext();
        var repo = new EgressRunRepository(context);
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        context.EgressRemoveLearners.Add(new EgressRemoveLearner
        {
            Id = Guid.NewGuid(), RunId = id, ChangeRequestId = Guid.NewGuid(), TicketId = 9999, ReferenceNumber = "STALE",
            CorrectionId = "9999", CorrectionType = "31", CorrectionReason = "4", KeyStage = "KS4", EstablishmentNumber = "4070",
            Surname = "Stale", Forename = "Entity", Sex = "F", DateOfBirth = "2010-01-01", CycleYear = "2026", CycleMonth = "6",
            LocalAuthority = "860", LearnerId = "555"
        });
        var remove = new RemoveLearnerRow("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, "REF-1");
        var names = new Dictionary<EgressOutputType, string> { [EgressOutputType.RemoveLearners] = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv" };

        await repo.SavePreprocessedAsync(id, EgressRunStatus.Pulled, [], [remove], new DateOnly(2026, 6, 8), names, CancellationToken.None);

        var saved = await repo.GetRemoveLearnersAsync(id, CancellationToken.None);
        var only = Assert.Single(saved);
        Assert.Equal("REF-1", only.ReferenceNumber);
    }

    // S2 (second-pass nit): MarkTransferFailedAsync also writes an AuditEntry inside its retried
    // delegate but was missing the ChangeTracker.Clear() that SavePreprocessedAsync and
    // MarkTransferredAsync already have — the same retry-duplication hazard as the fact above,
    // reproduced the same way: a stale tracked AuditEntry left by an earlier attempt must not be
    // saved alongside the real one.
    [Fact]
    public async Task A_stale_tracked_audit_entry_left_by_an_earlier_attempt_is_not_saved_alongside_the_real_one()
    {
        await ResetAsync();
        var context = fixture.CreateContext();
        var repo = new EgressRunRepository(context);
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        context.AuditEntries.Add(new AuditEntry
        {
            EntityType = "EgressRun", EntityId = id.ToString(), Action = "TransferFailed",
            Timestamp = DateTime.UtcNow, UserId = UserId.ToString(), NewValues = "{}"
        });

        await repo.MarkTransferFailedAsync(id, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);

        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.EntityType == "EgressRun" && a.EntityId == id.ToString() && a.Action == "TransferFailed"));
    }

    // AB#294592: the audit log displays from the payload, so a failure row must say which window,
    // which files and who — as the success row always has. Read after the guarded flip, same
    // transaction.
    [Fact]
    public async Task A_failed_transfer_audit_row_names_the_window_output_types_and_person()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners, EgressOutputType.NewLearners), CancellationToken.None);

        await repo.MarkTransferFailedAsync(id, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);

        await using var db = fixture.CreateContext();
        var entry = await db.AuditEntries.SingleAsync(a => a.EntityType == "EgressRun" && a.EntityId == id.ToString() && a.Action == "TransferFailed");
        Assert.Equal(UserId.ToString(), entry.UserId);
        Assert.Contains("\"outcome\":\"Failed\"", entry.NewValues);
        Assert.Contains($"\"windowId\":\"{WindowId.ToString().ToLowerInvariant()}\"", entry.NewValues);
        Assert.Contains("\"outputTypes\":[\"NewLearners\",\"RemoveLearners\"]", entry.NewValues);
        Assert.Contains("\"transferredBy\":\"Ops One\"", entry.NewValues);
        Assert.Contains("\"reason\":\"Blob upload refused\"", entry.NewValues);
    }

    [Fact]
    public async Task Save_preprocessed_writes_rows_file_names_and_export_date_in_one_go()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners, EgressOutputType.NewLearners), CancellationToken.None);
        var remove = new RemoveLearnerRow("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, "REF-1")
            { YearGroup = "12", RemovalYear0 = "TRUE", RemovalYear1 = "FALSE", RemovalYear2 = "" };
        var add = new NewLearnerRow("1003", "10", "KS4", "860", "4070", "Jones", "Bob", "M", "2010-01-02", "2018-09-04", "", "2026", "6", "142313", "", "A860407000011", "", "10", Guid.NewGuid(), 1003, "REF-3");
        var names = new Dictionary<EgressOutputType, string>
        {
            [EgressOutputType.RemoveLearners] = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv",
            [EgressOutputType.NewLearners] = "CYPMD_LDS_KS4_NewLearners_2026_06_08.csv"
        };

        await repo.SavePreprocessedAsync(id, EgressRunStatus.Pulled, [add], [remove], new DateOnly(2026, 6, 8), names, CancellationToken.None);

        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Preprocessed, run!.Status);
        Assert.Equal(new DateOnly(2026, 6, 8), run.ExportDate);
        Assert.Equal("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", run.Outputs.Single(o => o.OutputType == EgressOutputType.RemoveLearners).FileName);
        Assert.Equal(1, run.Outputs.Single(o => o.OutputType == EgressOutputType.NewLearners).OutputRecordCount);
        Assert.Equal(remove, Assert.Single(await repo.GetRemoveLearnersAsync(id, CancellationToken.None)));
        Assert.Equal(add, Assert.Single(await repo.GetNewLearnersAsync(id, CancellationToken.None)));

        // Saving again (a re-run of preprocessing) replaces rather than duplicates.
        await repo.SavePreprocessedAsync(id, EgressRunStatus.Preprocessed, [add], [remove], new DateOnly(2026, 6, 8), names, CancellationToken.None);
        Assert.Single(await repo.GetRemoveLearnersAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Transferred_writes_the_audit_row_and_keeps_the_pair_blocked_forever()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var audit = new EgressTransferAudit(UserId.ToString(), "Ops One", "cypmd/extracts_input",
            new Dictionary<EgressOutputType, (string, int, string)> { [EgressOutputType.RemoveLearners] = ("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", 1, "ABC") });

        await repo.MarkTransferredAsync(id, EgressRunStatus.Pulled, audit, new DateTime(2026, 6, 8, 14, 38, 0, DateTimeKind.Utc), CancellationToken.None);

        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Transferred, run!.Status);
        Assert.Equal("Ops One", run.TransferredByName);
        Assert.Equal("ABC", run.Outputs[0].Sha256);
        var blocker = await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Transferred, blocker!.Status);
        Assert.NotNull(blocker.TransferredAtUtc);

        await using var db = fixture.CreateContext();
        var entry = await db.AuditEntries.SingleAsync(a => a.EntityType == "EgressRun" && a.EntityId == id.ToString() && a.Action == "Transfer");
        Assert.Contains("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", entry.NewValues);
        Assert.Equal(UserId.ToString(), entry.UserId);
    }

    [Fact]
    public async Task Transfer_failure_releases_the_pair_and_a_retry_can_reactivate_it_unless_taken()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);

        await repo.MarkTransferFailedAsync(id, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);
        Assert.Null(await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None));
        Assert.Equal("Blob upload refused", (await repo.GetRunAsync(id, CancellationToken.None))!.TransferFailureReason);

        Assert.Null(await repo.TryReactivateAsync(id, CancellationToken.None));            // retry allowed
        await repo.MarkTransferFailedAsync(id, EgressRunStatus.TransferFailed, "again", UserId.ToString(), "Ops One", CancellationToken.None);

        var newer = await Repository().CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var blocked = await repo.TryReactivateAsync(id, CancellationToken.None);           // pair taken
        Assert.Equal(EgressOutputType.RemoveLearners, blocked!.Value.OutputType);
        Assert.Equal(newer, blocked.Value.Blocker.RunId);

        await using var db = fixture.CreateContext();
        Assert.Equal(2, await db.AuditEntries.CountAsync(a => a.EntityType == "EgressRun" && a.EntityId == id.ToString() && a.Action == "TransferFailed"));
    }

    // S1: RawRecordsJson (the pulled payload — names, DOB, sex, UPN, every journey answer) must not
    // be copied into AuditEntries.NewValues on insert; only the run-level Transfer/TransferFailed
    // audit row (already asserted above) is the audit record this feature writes. Same rationale as
    // the existing EgressNewLearner/EgressRemoveLearner exemption, extended to the row that carries
    // the raw pull.
    [Fact]
    public async Task Creating_a_run_and_saving_preprocessed_rows_writes_no_audit_entry_for_any_egress_row()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var remove = new RemoveLearnerRow("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, "REF-1");
        var names = new Dictionary<EgressOutputType, string> { [EgressOutputType.RemoveLearners] = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv" };
        await repo.SavePreprocessedAsync(id, EgressRunStatus.Pulled, [], [remove], new DateOnly(2026, 6, 8), names, CancellationToken.None);

        await using var db = fixture.CreateContext();
        Assert.False(await db.AuditEntries.AnyAsync(a =>
            a.EntityType == "EgressRunOutput" || a.EntityType == "EgressNewLearner" || a.EntityType == "EgressRemoveLearner"));
    }

    // M4: every terminal write is guarded by the expected status it requires, so a run that has
    // moved on (e.g. Abandoned) since the caller last read it can never be silently overwritten.
    [Fact]
    public async Task MarkTransferred_after_abandon_is_a_no_op()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        Assert.Equal(1, await repo.AbandonAsync(id, CancellationToken.None));
        var audit = new EgressTransferAudit(UserId.ToString(), "Ops One", "cypmd/extracts_input",
            new Dictionary<EgressOutputType, (string, int, string)> { [EgressOutputType.RemoveLearners] = ("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", 1, "ABC") });

        var rows = await repo.MarkTransferredAsync(id, EgressRunStatus.Transferring, audit, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, rows);
        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Abandoned, run!.Status);
        Assert.Null(run.Outputs[0].FileName);
        await using var db = fixture.CreateContext();
        Assert.False(await db.AuditEntries.AnyAsync(a => a.EntityType == "EgressRun" && a.EntityId == id.ToString() && a.Action == "Transfer"));
    }

    [Fact]
    public async Task Abandon_after_transferred_is_a_no_op()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var audit = new EgressTransferAudit(UserId.ToString(), "Ops One", "cypmd/extracts_input",
            new Dictionary<EgressOutputType, (string, int, string)> { [EgressOutputType.RemoveLearners] = ("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", 1, "ABC") });
        Assert.Equal(1, await repo.MarkTransferredAsync(id, EgressRunStatus.Pulled, audit, DateTime.UtcNow, CancellationToken.None));

        var rows = await repo.AbandonAsync(id, CancellationToken.None);

        Assert.Equal(0, rows);
        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Transferred, run!.Status);
        Assert.True(run.Outputs[0].IsActive);
    }

    // M4: a run stuck in Preprocessing (a pod restart mid-pipeline) must always be releasable —
    // the lock has no expiry and no other override.
    [Fact]
    public async Task Abandon_admits_a_run_stuck_in_preprocessing()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        Assert.True(await repo.TrySetStatusAsync(id, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, CancellationToken.None));

        var rows = await repo.AbandonAsync(id, CancellationToken.None);

        Assert.Equal(1, rows);
        Assert.Equal(EgressRunStatus.Abandoned, (await repo.GetRunAsync(id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task SavePreprocessed_after_abandon_is_a_no_op()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        Assert.Equal(1, await repo.AbandonAsync(id, CancellationToken.None));
        var remove = new RemoveLearnerRow("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, "REF-1");
        var names = new Dictionary<EgressOutputType, string> { [EgressOutputType.RemoveLearners] = "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv" };

        var rows = await repo.SavePreprocessedAsync(id, EgressRunStatus.Preprocessing, [], [remove], new DateOnly(2026, 6, 8), names, CancellationToken.None);

        Assert.Equal(0, rows);
        var run = await repo.GetRunAsync(id, CancellationToken.None);
        Assert.Equal(EgressRunStatus.Abandoned, run!.Status);
        Assert.Empty(await repo.GetRemoveLearnersAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Candidates_are_the_windows_amendments_of_that_type_whatever_their_ticket_state()
    {
        await ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            ChangeRequest Row(string reference, WhatToChange type, RequestStatus status, string? crm) => new()
            {
                Id = Guid.NewGuid(), WindowId = WindowId, OrganisationUrn = 142313, OrganisationLaestab = "860/4070",
                Submitted = new DateTime(2026, 6, 5, 9, 0, 0), SubmittedById = UserId, SubmittedByName = "School User",
                Status = status, ReferenceNumber = reference, RequestType = RequestType.Amendment,
                RequestTypeDescription = type.ToString(), AmendmentType = type, CrmId = crm
            };
            db.ChangeRequests.AddRange(
                Row("C-REMOVE-COMMITTED", WhatToChange.Remove, RequestStatus.SubmittedCommitted, "2001"),
                Row("C-REMOVE-UNCOMMITTED", WhatToChange.Remove, RequestStatus.SubmittedUnCommitted, null),
                Row("C-REMOVE-WITHDRAWN", WhatToChange.Remove, RequestStatus.Withdrawn, null),
                Row("C-ADD", WhatToChange.Add, RequestStatus.SubmittedCommitted, "2002"));
            await db.SaveChangesAsync();
        }

        var removes = await Repository().GetCandidateRequestsAsync(WindowId, WhatToChange.Remove, CancellationToken.None);

        Assert.Equal(["C-REMOVE-COMMITTED", "C-REMOVE-UNCOMMITTED"], removes.Select(r => r.ReferenceNumber).Order());
        Assert.Equal("860/4070", removes[0].OrganisationLaestab);
    }

    [Fact]
    public async Task List_returns_newest_first_with_window_title_and_output_types()
    {
        await ResetAsync();
        var repo = Repository();
        var older = await repo.CreateRunAsync(Create(EgressOutputType.NewLearners), CancellationToken.None);
        await repo.AbandonAsync(older, CancellationToken.None);
        var newer = await repo.CreateRunAsync(Create(EgressOutputType.NewLearners, EgressOutputType.RemoveLearners), CancellationToken.None);

        var list = (await repo.ListRunsAsync(CancellationToken.None)).Where(r => r.WindowId == WindowId).ToList();

        Assert.Equal([newer, older], list.Select(r => r.Id));
        Assert.Equal("Egress repo window", list[0].WindowTitle);
        Assert.Equal([EgressOutputType.NewLearners, EgressOutputType.RemoveLearners], list[0].OutputTypes);
        Assert.Equal(EgressRunStatus.Abandoned, list[1].Status);
    }

    [Fact]
    public async Task Status_transitions_are_conditional_on_the_expected_current_state()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);

        Assert.True(await repo.TrySetStatusAsync(id, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, CancellationToken.None));
        Assert.False(await repo.TrySetStatusAsync(id, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, CancellationToken.None));
        Assert.Equal(EgressRunStatus.Preprocessing, (await repo.GetRunAsync(id, CancellationToken.None))!.Status);
    }

    // ---------------------------------------------------------------- Runs history (AB#294590)

    // Every status appears, newest StartedAtUtc first; ties break on Id so paging is stable.
    [Fact]
    public async Task History_lists_every_status_newest_first_with_the_id_as_tiebreak()
    {
        await ResetAsync();
        var repo = Repository();
        var abandoned = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.AbandonAsync(abandoned, CancellationToken.None);
        var failed = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.MarkTransferFailedAsync(failed, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);
        var transferred = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.MarkTransferredAsync(transferred, EgressRunStatus.Pulled, Audit(0), DateTime.UtcNow, CancellationToken.None);
        var draft = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.NewLearners), CancellationToken.None);

        var page = await repo.ListHistoryAsync(In(WindowId), 1, 20, CancellationToken.None);

        Assert.Equal(4, page.TotalCount);
        Assert.Equal([draft, transferred, failed, abandoned], page.Rows.Select(r => r.Id));
        Assert.Equal([EgressRunStatus.Pulled, EgressRunStatus.Transferred, EgressRunStatus.TransferFailed, EgressRunStatus.Abandoned], page.Rows.Select(r => r.Status));
        Assert.All(page.Rows, r => Assert.Equal("Egress repo window", r.WindowTitle));

        // Force every run onto one timestamp: the order must then be Id descending, not arbitrary.
        await using var db = fixture.CreateContext();
        var sameInstant = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
        await db.EgressRuns.Where(r => r.WindowId == WindowId).ExecuteUpdateAsync(s => s.SetProperty(r => r.StartedAtUtc, sameInstant));
        var tied = await Repository().ListHistoryAsync(In(WindowId), 1, 20, CancellationToken.None);
        var expected = new[] { draft, transferred, failed, abandoned }.OrderByDescending(id => id).ToList();
        Assert.Equal(expected, tied.Rows.Select(r => r.Id));
    }

    // "Records" answers what LDS received: only a transferred run reports its saved count.
    [Fact]
    public async Task History_reports_records_transferred_only_for_a_transferred_run()
    {
        await ResetAsync();
        var repo = Repository();
        var failedAfterSave = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.SavePreprocessedAsync(failedAfterSave, EgressRunStatus.Pulled, [], [RemoveRow("REF-1"), RemoveRow("REF-2")], new DateOnly(2026, 6, 8), RemoveFileName, CancellationToken.None);
        await repo.MarkTransferFailedAsync(failedAfterSave, EgressRunStatus.Preprocessed, "Blob upload refused", UserId.ToString(), "Ops One", CancellationToken.None);
        var transferred = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.SavePreprocessedAsync(transferred, EgressRunStatus.Pulled, [], [RemoveRow("REF-1"), RemoveRow("REF-2"), RemoveRow("REF-3")], new DateOnly(2026, 6, 8), RemoveFileName, CancellationToken.None);
        await repo.MarkTransferredAsync(transferred, EgressRunStatus.Preprocessed, Audit(3), DateTime.UtcNow, CancellationToken.None);
        var draft = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.NewLearners), CancellationToken.None);

        var rows = (await repo.ListHistoryAsync(In(WindowId), 1, 20, CancellationToken.None)).Rows.ToDictionary(r => r.Id);

        Assert.Equal(3, rows[transferred].RecordsTransferred);
        Assert.Equal(0, rows[failedAfterSave].RecordsTransferred);   // rows were saved, nothing was sent
        Assert.Equal(0, rows[draft].RecordsTransferred);
    }

    [Fact]
    public async Task History_lists_every_output_type_a_run_covered()
    {
        await ResetAsync();
        var repo = Repository();
        var both = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners, EgressOutputType.NewLearners), CancellationToken.None);

        var row = Assert.Single((await repo.ListHistoryAsync(In(WindowId), 1, 20, CancellationToken.None)).Rows);

        Assert.Equal(both, row.Id);
        Assert.Equal([EgressOutputType.NewLearners, EgressOutputType.RemoveLearners], row.OutputTypes);
        Assert.Equal("Ops One", row.StartedByName);
    }

    // Filters are cumulative (ticket "Filtering"): window alone, status alone, both together.
    [Fact]
    public async Task History_filters_by_window_and_by_outcome_cumulatively()
    {
        await ResetAsync();
        var repo = Repository();
        var w1Abandoned = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.AbandonAsync(w1Abandoned, CancellationToken.None);
        var w1Draft = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);
        var w2Abandoned = await repo.CreateRunAsync(CreateIn(WindowId2, EgressOutputType.RemoveLearners), CancellationToken.None);
        await repo.AbandonAsync(w2Abandoned, CancellationToken.None);
        var w2Draft = await repo.CreateRunAsync(CreateIn(WindowId2, EgressOutputType.RemoveLearners), CancellationToken.None);

        var window2 = await repo.ListHistoryAsync(new EgressRunHistoryFilter(WindowId2, null), 1, 20, CancellationToken.None);
        Assert.Equal([w2Draft, w2Abandoned], window2.Rows.Select(r => r.Id));
        Assert.Equal(2, window2.TotalCount);

        // Status alone spans windows; other test classes may own runs in other windows, so only
        // membership of this class's runs is asserted, not the total.
        var abandonedOnly = await repo.ListHistoryAsync(new EgressRunHistoryFilter(null, EgressRunOutcome.Abandoned), 1, 200, CancellationToken.None);
        Assert.Contains(w1Abandoned, abandonedOnly.Rows.Select(r => r.Id));
        Assert.Contains(w2Abandoned, abandonedOnly.Rows.Select(r => r.Id));
        Assert.DoesNotContain(w1Draft, abandonedOnly.Rows.Select(r => r.Id));
        Assert.DoesNotContain(w2Draft, abandonedOnly.Rows.Select(r => r.Id));

        var both = await repo.ListHistoryAsync(new EgressRunHistoryFilter(WindowId, EgressRunOutcome.Draft), 1, 20, CancellationToken.None);
        Assert.Equal([w1Draft], both.Rows.Select(r => r.Id));
        Assert.Equal(1, both.TotalCount);
    }

    [Fact]
    public async Task History_pages_twenty_at_a_time_and_clamps_an_out_of_range_page()
    {
        await ResetAsync();
        var repo = Repository();
        for (var i = 0; i < 25; i++)
        {
            var id = await repo.CreateRunAsync(CreateIn(WindowId2, EgressOutputType.RemoveLearners), CancellationToken.None);
            await repo.AbandonAsync(id, CancellationToken.None);   // releases the pair for the next one
        }

        var first = await repo.ListHistoryAsync(In(WindowId2), 1, 20, CancellationToken.None);
        Assert.Equal(20, first.Rows.Count);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(1, first.Page);
        Assert.Equal(20, first.PageSize);

        var second = await repo.ListHistoryAsync(In(WindowId2), 2, 20, CancellationToken.None);
        Assert.Equal(5, second.Rows.Count);
        Assert.Equal(2, second.Page);
        Assert.Empty(first.Rows.Select(r => r.Id).Intersect(second.Rows.Select(r => r.Id)));

        var beyond = await repo.ListHistoryAsync(In(WindowId2), 99, 20, CancellationToken.None);
        Assert.Equal(2, beyond.Page);
        Assert.Equal(second.Rows.Select(r => r.Id), beyond.Rows.Select(r => r.Id));

        var below = await repo.ListHistoryAsync(In(WindowId2), 0, 20, CancellationToken.None);
        Assert.Equal(1, below.Page);
        Assert.Equal(first.Rows.Select(r => r.Id), below.Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task History_with_no_match_is_an_empty_first_page()
    {
        await ResetAsync();
        var repo = Repository();
        var draft = await repo.CreateRunAsync(CreateIn(WindowId, EgressOutputType.RemoveLearners), CancellationToken.None);

        var page = await repo.ListHistoryAsync(new EgressRunHistoryFilter(WindowId, EgressRunOutcome.Success), 1, 20, CancellationToken.None);

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.TotalPages);
        Assert.NotEqual(Guid.Empty, draft);
    }
}
