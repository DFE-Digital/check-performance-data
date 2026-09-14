using System.Text;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// Transfer is atomic from the user's point of view: every file lands or none does, the database
// is the source of the files (never the pulled payload), and the audit row is written by the
// repository in the same transaction as the run state.
public sealed class EgressTransferServiceTests
{
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly EgressActor Actor = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Ops One", "ops@example.com");
    private readonly IEgressRunRepository _repo = Substitute.For<IEgressRunRepository>();
    private readonly IEgressBlobClient _blobs = Substitute.For<IEgressBlobClient>();

    private EgressTransferService Sut() => new(_repo, _blobs, Substitute.For<ILogger<EgressTransferService>>());

    private static RemoveLearnerRow RemoveRow(string ticket) =>
        new(ticket, "31", "4", "KS4", "4070", "Smith", "Alice", "F", "2010-09-07", "2026", "6", "860", "555", Guid.NewGuid(), long.Parse(ticket), $"REF-{ticket}");
    private static NewLearnerRow NewRow(string ticket) =>
        new(ticket, "10", "KS4", "860", "4070", "Jones", "", "Bob", "M", "2010-01-02", "2018-09-04", "", "2026", "6", "142313", "", "A860407000011", "", "10", "N", Guid.NewGuid(), long.Parse(ticket), $"REF-{ticket}");

    private void RunIs(EgressRunStatus status, params EgressOutputType[] types)
    {
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressRunDto(RunId, Guid.NewGuid(), status, Guid.NewGuid(), "Ops One",
            DateTime.UtcNow, DateTime.UtcNow, new DateOnly(2026, 6, 8), null, null, [], null,
            types.Select(t => new EgressRunOutputDto(Guid.NewGuid(), t, true, [], 2, 2,
                $"CYPMD_LDS_KS4_{EgressOutputTypes.FileToken(t)}_2026_06_08.csv", null)).ToList()));
        _repo.GetRemoveLearnersAsync(RunId, Arg.Any<CancellationToken>()).Returns([RemoveRow("1001"), RemoveRow("1002")]);
        _repo.GetNewLearnersAsync(RunId, Arg.Any<CancellationToken>()).Returns([NewRow("2001")]);
        _blobs.IsConfigured.Returns(true);
        _blobs.TargetDescription.Returns("cypmd/extracts_input");
        // M4: the guarded writes now return rows affected — default to "won the race" (1) so
        // existing success-path tests are unaffected; the specific lost-the-race tests override this.
        _repo.MarkTransferredAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<EgressTransferAudit>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);
        _repo.AbandonAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1);
    }

    [Fact]
    public async Task Uploads_every_file_from_the_database_rows_then_marks_transferred_with_the_audit()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        var uploaded = new Dictionary<string, string>();
        await _blobs.UploadAsync(Arg.Do<string>(n => uploaded[n] = ""), Arg.Do<byte[]>(b => uploaded[uploaded.Keys.Last()] = Encoding.UTF8.GetString(b)), Arg.Any<string>(), RunId, Arg.Any<CancellationToken>());

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        var ok = Assert.IsType<EgressTransferResult.Transferred>(result);
        Assert.Equal(2, ok.Files.Count);
        Assert.StartsWith("Correction_ID,Correction_Type,Correction_Reason,", uploaded["CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv"]);
        Assert.Contains("\r\n1001,31,4,KS4,4070,Smith,Alice,F,2010-09-07,2026,6,860,555", uploaded["CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv"]);
        Assert.StartsWith("Correction_ID,Correction_Type,Key_Stage,", uploaded["CYPMD_LDS_KS4_NewLearners_2026_06_08.csv"]);
        await _repo.Received(1).MarkTransferredAsync(RunId, EgressRunStatus.Transferring,
            Arg.Is<EgressTransferAudit>(a => a.UserName == "Ops One" && a.TargetContainer == "cypmd/extracts_input"
                && a.Files[EgressOutputType.RemoveLearners].Records == 2 && a.Files[EgressOutputType.NewLearners].Records == 1
                && a.Files[EgressOutputType.RemoveLearners].Sha256.Length == 64),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().MarkTransferFailedAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_failure_on_the_second_file_deletes_the_first_and_marks_the_run_failed()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _blobs.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<byte[]>(), Arg.Any<string>(), RunId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new EgressBlobAlreadyExistsException("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv")));

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        var failed = Assert.IsType<EgressTransferResult.Failed>(result);
        Assert.Contains("already exists", failed.Reason);
        await _blobs.Received(1).DeleteIfExistsAsync("CYPMD_LDS_KS4_NewLearners_2026_06_08.csv", Arg.Any<CancellationToken>());
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Transferring, Arg.Is<string>(r => r.Contains("already exists")), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().MarkTransferredAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task Unconfigured_storage_fails_before_anything_is_uploaded()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.RemoveLearners);
        _blobs.IsConfigured.Returns(false);

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        var failed = Assert.IsType<EgressTransferResult.Failed>(result);
        Assert.Contains("not configured", failed.Reason);
        await _blobs.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default, default);
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Preprocessed, Arg.Any<string>(), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_retry_after_failure_reactivates_first_and_is_refused_when_the_pair_is_taken()
    {
        RunIs(EgressRunStatus.TransferFailed, EgressOutputType.RemoveLearners);
        var blocker = new EgressBlocker(Guid.NewGuid(), EgressRunStatus.Pulled, "Ops Two", DateTime.UtcNow, null, null);
        _repo.TryReactivateAsync(RunId, Arg.Any<CancellationToken>()).Returns((EgressOutputType.RemoveLearners, blocker));

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        var refused = Assert.IsType<EgressTransferResult.Refused>(result);
        Assert.Equal(blocker, refused.Blocker);
        Assert.Equal(EgressOutputType.RemoveLearners, refused.OutputType);
        await _blobs.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default, default);
    }

    [Theory]
    [InlineData(EgressRunStatus.Pulled)]
    [InlineData(EgressRunStatus.Transferred)]
    [InlineData(EgressRunStatus.Abandoned)]
    public async Task Only_a_preprocessed_or_failed_run_can_be_transferred(EgressRunStatus status)
    {
        RunIs(status, EgressOutputType.RemoveLearners);
        Assert.Equal(status, Assert.IsType<EgressTransferResult.NotTransferable>(await Sut().TransferAsync(RunId, Actor, CancellationToken.None)).Status);
    }

    // M3: a run whose approved set is empty (every record rejected, undecided, or lost to B1) must
    // not send a header-only file and lock the pair forever — this must refuse before any upload.
    [Fact]
    public async Task A_run_whose_every_output_has_zero_saved_rows_refuses_before_any_upload()
    {
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressRunDto(RunId, Guid.NewGuid(), EgressRunStatus.Preprocessed, Guid.NewGuid(), "Ops One",
            DateTime.UtcNow, DateTime.UtcNow, new DateOnly(2026, 6, 8), null, null, [], null,
            [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 3, 0, "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", null)]));

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        Assert.IsType<EgressTransferResult.NothingToTransfer>(result);
        await _blobs.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default, default);
        await _repo.DidNotReceiveWithAnyArgs().TrySetStatusAsync(default, default, default, default);
    }

    [Fact]
    public async Task A_run_with_at_least_one_non_empty_output_is_still_transferable()
    {
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressRunDto(RunId, Guid.NewGuid(), EgressRunStatus.Preprocessed, Guid.NewGuid(), "Ops One",
            DateTime.UtcNow, DateTime.UtcNow, new DateOnly(2026, 6, 8), null, null, [], null,
            [
                new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 3, 0, "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", null),
                new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.NewLearners, true, [], 1, 1, "CYPMD_LDS_KS4_NewLearners_2026_06_08.csv", null)
            ]));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetRemoveLearnersAsync(RunId, Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetNewLearnersAsync(RunId, Arg.Any<CancellationToken>()).Returns([NewRow("2001")]);
        _blobs.IsConfigured.Returns(true);
        _repo.MarkTransferredAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<EgressTransferAudit>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        Assert.IsType<EgressTransferResult.Transferred>(result);
    }

    // M4: MarkTransferredAsync's own status guard can lose the race even without throwing (e.g.
    // an Abandon landed between the Transferring flip and this write) — 0 rows means compensate,
    // never report Transferred.
    [Fact]
    public async Task Zero_rows_from_MarkTransferredAsync_compensates_without_reporting_transferred()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _repo.MarkTransferredAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<EgressTransferAudit>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0);

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        Assert.IsType<EgressTransferResult.Failed>(result);
        await _blobs.Received(1).DeleteIfExistsAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<CancellationToken>());
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Transferring, Arg.Any<string>(), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFile_renders_the_persisted_rows_for_preview_and_download()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.RemoveLearners);
        var text = Encoding.UTF8.GetString(await Sut().BuildFileAsync(RunId, EgressOutputType.RemoveLearners, CancellationToken.None));
        Assert.Equal(3, text.Split("\r\n").Length);
        Assert.DoesNotContain("\n\n", text);
    }

    // M1: once the CAS flip to Transferring has happened, cancellation of the caller's token must
    // not abandon a run with files already uploaded — the upload/commit phase and its compensation
    // run with CancellationToken.None.
    [Fact]
    public async Task After_the_status_flips_to_transferring_the_callers_cancellation_no_longer_skips_compensation()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        using var cts = new CancellationTokenSource();
        _blobs.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<byte[]>(), Arg.Any<string>(), RunId, Arg.Any<CancellationToken>())
            .Returns(_ => { cts.Cancel(); throw new OperationCanceledException("client disconnected"); });
        CancellationToken? deleteToken = null;
        _blobs.DeleteIfExistsAsync(Arg.Any<string>(), Arg.Do<CancellationToken>(t => deleteToken = t)).Returns(Task.CompletedTask);
        CancellationToken? markFailedToken = null;
        _repo.MarkTransferFailedAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<CancellationToken>(t => markFailedToken = t))
            .Returns(1);

        var result = await Sut().TransferAsync(RunId, Actor, cts.Token);

        Assert.IsType<EgressTransferResult.Failed>(result);
        await _blobs.Received(1).DeleteIfExistsAsync("CYPMD_LDS_KS4_NewLearners_2026_06_08.csv", Arg.Any<CancellationToken>());
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Transferring, Arg.Any<string>(), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
        Assert.Equal(CancellationToken.None, deleteToken);
        Assert.Equal(CancellationToken.None, markFailedToken);
    }

    // M1: a post-upload DB failure (every file landed, but the commit that marks Transferred
    // threw) must not leave the run silently stuck in Transferring with no audit trail.
    [Fact]
    public async Task MarkTransferredAsync_throwing_after_every_upload_succeeds_compensates_and_marks_transfer_failed()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _repo.MarkTransferredAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<EgressTransferAudit>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("database unreachable")));

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        Assert.IsType<EgressTransferResult.Failed>(result);
        await _blobs.Received(1).DeleteIfExistsAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<CancellationToken>());
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Transferring, Arg.Any<string>(), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().TryReactivateAsync(default, default);
    }

    // M1/S3: the compensation delete itself can fail (the ops user has no LDS access to fix it by
    // hand) — the reason must say so rather than silently dropping the detail.
    [Fact]
    public async Task If_the_compensation_delete_itself_fails_the_reason_explains_manual_cleanup_is_needed()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _blobs.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<byte[]>(), Arg.Any<string>(), RunId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new EgressBlobAlreadyExistsException("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv")));
        _blobs.DeleteIfExistsAsync("CYPMD_LDS_KS4_NewLearners_2026_06_08.csv", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("network blip")));

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        var failed = Assert.IsType<EgressTransferResult.Failed>(result);
        Assert.Contains("remove it by hand", failed.Reason);
        await _repo.Received(1).MarkTransferFailedAsync(RunId, EgressRunStatus.Transferring, Arg.Is<string>(r => r.Contains("remove it by hand")), Actor.UserId.ToString(), Arg.Any<CancellationToken>());
    }

    // S3: a PUT that succeeded server-side but whose response was lost must not leave an orphan
    // that only LDS (not the ops user) can remove — the failing file itself is swept too, but only
    // if it is stamped as this run's own.
    [Fact]
    public async Task S3_a_failed_upload_also_removes_its_own_blob_if_the_write_actually_landed()
    {
        RunIs(EgressRunStatus.Preprocessed, EgressOutputType.RemoveLearners);
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, Arg.Any<CancellationToken>()).Returns(true);
        _blobs.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", Arg.Any<byte[]>(), Arg.Any<string>(), RunId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("response lost")));
        _blobs.DeleteIfOwnedByRunAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", RunId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Sut().TransferAsync(RunId, Actor, CancellationToken.None);

        Assert.IsType<EgressTransferResult.Failed>(result);
        await _blobs.Received(1).DeleteIfOwnedByRunAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", RunId, Arg.Any<CancellationToken>());
    }

    // M1: Abandon during Transferring — the lock has no expiry, so a run stuck there (a pod
    // restart mid-upload) must be releasable, and any blob it actually wrote must be swept so it
    // does not block a same-named retry, without ever touching a blob owned by another run.
    [Fact]
    public async Task Abandoning_a_transferring_run_sweeps_only_the_blobs_this_run_owns()
    {
        RunIs(EgressRunStatus.Transferring, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners);
        _blobs.DeleteIfOwnedByRunAsync("CYPMD_LDS_KS4_NewLearners_2026_06_08.csv", RunId, Arg.Any<CancellationToken>()).Returns(true);
        _blobs.DeleteIfOwnedByRunAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", RunId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sut().AbandonAsync(RunId, CancellationToken.None);

        var abandoned = Assert.IsType<EgressAbandonResult.Abandoned>(result);
        Assert.Equal(["CYPMD_LDS_KS4_NewLearners_2026_06_08.csv"], abandoned.RemovedFiles);
        await _repo.Received(1).AbandonAsync(RunId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abandoning_a_run_that_is_not_transferring_never_touches_blob_storage()
    {
        RunIs(EgressRunStatus.Pulled, EgressOutputType.RemoveLearners);

        var result = await Sut().AbandonAsync(RunId, CancellationToken.None);

        var abandoned = Assert.IsType<EgressAbandonResult.Abandoned>(result);
        Assert.Empty(abandoned.RemovedFiles);
        await _blobs.DidNotReceiveWithAnyArgs().DeleteIfOwnedByRunAsync(default!, default, default);
        await _repo.Received(1).AbandonAsync(RunId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abandoning_an_already_transferred_run_is_refused()
    {
        RunIs(EgressRunStatus.Transferred, EgressOutputType.RemoveLearners);

        var result = await Sut().AbandonAsync(RunId, CancellationToken.None);

        Assert.IsType<EgressAbandonResult.AlreadyTransferred>(result);
        await _repo.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
    }

    [Fact]
    public async Task Abandoning_an_unknown_run_reports_not_found()
    {
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns((EgressRunDto?)null);

        var result = await Sut().AbandonAsync(RunId, CancellationToken.None);

        Assert.IsType<EgressAbandonResult.NotFound>(result);
    }
}
