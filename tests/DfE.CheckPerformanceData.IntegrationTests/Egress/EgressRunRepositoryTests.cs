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
    private static readonly Guid UserId = Guid.Parse("A0000000-0000-0000-0000-00000000E6AA");

    private EgressRunRepository Repository() => new(fixture.CreateContext());

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        if (!await db.CheckingWindows.AnyAsync(w => w.Id == WindowId))
        {
            db.CheckingWindows.Add(new CheckingWindow
            {
                Id = WindowId, Title = "Egress repo window", KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
            });
            await db.SaveChangesAsync();
        }
        await db.EgressRuns.Where(r => r.WindowId == WindowId).ExecuteDeleteAsync();
        await db.ChangeRequests.Where(r => r.WindowId == WindowId).ExecuteDeleteAsync();
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

    [Fact]
    public async Task Save_preprocessed_writes_rows_file_names_and_export_date_in_one_go()
    {
        await ResetAsync();
        var repo = Repository();
        var id = await repo.CreateRunAsync(Create(EgressOutputType.RemoveLearners, EgressOutputType.NewLearners), CancellationToken.None);
        var remove = new RemoveLearnerRow("1001", "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), 1001, "REF-1");
        var add = new NewLearnerRow("1003", "10", "KS4", "860", "4070", "Jones", "", "Bob", "M", "2010-01-02", "2018-09-04", "", "2026", "6", "142313", "", "A860407000011", "", "10", "N", Guid.NewGuid(), 1003, "REF-3");
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

        await repo.MarkTransferFailedAsync(id, EgressRunStatus.Pulled, "Blob upload refused", UserId.ToString(), CancellationToken.None);
        Assert.Null(await repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, CancellationToken.None));
        Assert.Equal("Blob upload refused", (await repo.GetRunAsync(id, CancellationToken.None))!.TransferFailureReason);

        Assert.Null(await repo.TryReactivateAsync(id, CancellationToken.None));            // retry allowed
        await repo.MarkTransferFailedAsync(id, EgressRunStatus.TransferFailed, "again", UserId.ToString(), CancellationToken.None);

        var newer = await Repository().CreateRunAsync(Create(EgressOutputType.RemoveLearners), CancellationToken.None);
        var blocker = await repo.TryReactivateAsync(id, CancellationToken.None);           // pair taken
        Assert.Equal(newer, blocker!.RunId);

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
}
