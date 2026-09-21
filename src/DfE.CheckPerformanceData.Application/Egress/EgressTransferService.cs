using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The Transfer step (AB#294553). Files are built from the persisted rows — the database is the
/// source of truth and a retry needs no re-pull. Uploads are create-only; if any file fails, every
/// file already written is deleted and the run is marked TransferFailed, so LDS never sees a
/// partial set. Success and failure each write an audit row (repository, same transaction as the
/// state change); only success is ever recorded as such.
/// </summary>
public sealed class EgressTransferService(IEgressRunRepository repository, IEgressBlobClient blobs, IWindowService windows, ILogger<EgressTransferService> logger) : IEgressTransferService
{
    public async Task<EgressTransferResult> TransferAsync(Guid runId, EgressActor actor, CancellationToken ct)
    {
        var run = await repository.GetRunAsync(runId, ct);
        if (run is null) return new EgressTransferResult.NotFound();
        if (run.Status is not (EgressRunStatus.Preprocessed or EgressRunStatus.TransferFailed))
            return new EgressTransferResult.NotTransferable(run.Status);

        // M3: an approved set that preprocessed to zero saved rows (every record rejected,
        // undecided, or lost to a misconfigured ticket source) must never send a header-only file
        // and lock the pair forever — refuse before touching storage or flipping status.
        if (run.Outputs.All(o => (o.OutputRecordCount ?? 0) == 0))
            return new EgressTransferResult.NothingToTransfer();

        var windowType = await WindowTypeAsync(run.WindowId, runId, ct);
        var fromStatus = run.Status;
        if (fromStatus == EgressRunStatus.TransferFailed)
        {
            var blocked = await repository.TryReactivateAsync(runId, ct);
            if (blocked is { } b) return new EgressTransferResult.Refused(b.OutputType, b.Blocker);
        }

        if (!blobs.IsConfigured)
            return await FailAsync(runId, actor, "Egress storage is not configured for this environment (ConnectionStrings:EgressStorage).", [], null, fromStatus, ct);

        if (!await repository.TrySetStatusAsync(runId, fromStatus, EgressRunStatus.Transferring, ct))
            return new EgressTransferResult.NotTransferable(EgressRunStatus.Transferring);

        // M1: once the run has flipped to Transferring, the caller's token (RequestAborted — a
        // closed browser tab) must not be able to leave the run half-transferred. Everything from
        // here on, including compensation, runs to completion regardless of the caller's token.
        var operationCt = CancellationToken.None;

        var files = new Dictionary<EgressOutputType, (string FileName, int Records, string Sha256)>();
        var uploaded = new List<string>();
        string? inFlight = null;
        try
        {
            foreach (var output in run.Outputs)
            {
                var (bytes, records) = await BuildAsync(runId, windowType, output.OutputType, operationCt);
                var fileName = output.FileName ?? throw new InvalidOperationException($"Run {runId} has no file name for {output.OutputType}.");
                inFlight = fileName;
                var sha = Convert.ToHexString(SHA256.HashData(bytes));
                await UploadReclaimingLeftoversAsync(fileName, bytes, sha, runId, operationCt);
                inFlight = null;
                uploaded.Add(fileName);
                files[output.OutputType] = (fileName, records, sha);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Egress run {RunId} transfer failed after {Uploaded} file(s); compensating", runId, uploaded.Count);
            return await FailAsync(runId, actor, ex.Message, uploaded, inFlight, EgressRunStatus.Transferring, operationCt);
        }

        DateTime at;
        int rows;
        try
        {
            at = DateTime.UtcNow;
            rows = await repository.MarkTransferredAsync(runId, EgressRunStatus.Transferring,
                new EgressTransferAudit(actor.UserId.ToString(), actor.DisplayName, blobs.TargetDescription, files), at, operationCt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Egress run {RunId}: MarkTransferredAsync failed after every file was uploaded; compensating", runId);
            return await FailAsync(runId, actor, ex.Message, uploaded, null, EgressRunStatus.Transferring, operationCt);
        }
        if (rows == 0)
        {
            // M4: lost the race — something else (an Abandon) moved the run off Transferring
            // between the CAS flip and this write. Compensate the uploads; no Succeeded audit row
            // is written because MarkTransferredAsync's own guard already refused to write one.
            logger.LogWarning("Egress run {RunId} was no longer Transferring when the transfer completed; compensating", runId);
            return await FailAsync(runId, actor, "This run was abandoned while the transfer was in progress.", uploaded, null, EgressRunStatus.Transferring, operationCt);
        }
        return new EgressTransferResult.Transferred(files.Select(f => (f.Key, f.Value.FileName, f.Value.Records)).ToList(), at);
    }

    // Follow-up to R1 (the Abandon crash window) and to a compensation delete that failed with
    // "remove it by hand": a same-named file already in the container is reclaimed — deleted and
    // the create-only upload retried exactly once — only when its egressRunId stamp names an
    // ABANDONED run (a sweep the process died in the middle of) or this very run (an earlier
    // attempt). Both are files nobody wants in LDS and neither has any other UI path out. A file
    // stamped by any live run, or carrying no stamp at all, is a real collision: left untouched,
    // and the exception propagates to FailAsync as before.
    private async Task UploadReclaimingLeftoversAsync(string fileName, byte[] bytes, string sha, Guid runId, CancellationToken ct)
    {
        try
        {
            await blobs.UploadAsync(fileName, bytes, sha, runId, ct);
            return;
        }
        catch (EgressBlobAlreadyExistsException)
        {
            if (await blobs.GetOwnerRunIdAsync(fileName, ct) is not { } owner) throw;
            var reclaimable = owner == runId || (await repository.GetRunAsync(owner, ct))?.Status == EgressRunStatus.Abandoned;
            if (!reclaimable) throw;
            // Ownership is re-checked at delete time; a false here means the file changed hands
            // between the two calls, which is the live-collision case again.
            if (!await blobs.DeleteIfOwnedByRunAsync(fileName, owner, ct)) throw;
            logger.LogWarning("Egress run {RunId}: reclaimed {File}, a leftover stamped by run {Owner}, before uploading", runId, fileName, owner);
        }
        await blobs.UploadAsync(fileName, bytes, sha, runId, ct);
    }

    // possiblyOrphaned (S3): the file that was mid-upload when the exception was thrown, if any —
    // its PUT may have actually landed server-side with the response lost to the client, so it is
    // worth a metadata-checked delete attempt distinct from `uploaded` (which this call definitely
    // wrote, since a create-only PUT that returned success cannot belong to another run).
    // expectedStatus (M4): the status MarkTransferFailedAsync's own guard requires — Transferring
    // once the CAS flip has happened, or the run's pre-flip status for the unconfigured-storage
    // refusal, which never flips at all.
    private async Task<EgressTransferResult> FailAsync(Guid runId, EgressActor actor, string reason, IReadOnlyList<string> uploaded, string? possiblyOrphaned, EgressRunStatus expectedStatus, CancellationToken ct)
    {
        foreach (var name in uploaded)
        {
            try { await blobs.DeleteIfExistsAsync(name, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Egress run {RunId}: could not delete {File} while compensating a failed transfer", runId, name);
                reason += $" Could not remove {name} from the target container — remove it by hand before retrying.";
            }
        }
        if (possiblyOrphaned is not null)
        {
            try
            {
                if (await blobs.DeleteIfOwnedByRunAsync(possiblyOrphaned, runId, ct))
                    logger.LogWarning("Egress run {RunId}: removed {File}, which had landed despite its upload response failing", runId, possiblyOrphaned);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Egress run {RunId}: could not check/remove {File} while compensating a failed transfer", runId, possiblyOrphaned);
                reason += $" Could not remove {possiblyOrphaned} from the target container — remove it by hand before retrying.";
            }
        }
        await repository.MarkTransferFailedAsync(runId, expectedStatus, reason, actor.UserId.ToString(), ct);
        return new EgressTransferResult.Failed(reason);
    }

    // M1: the lock has no expiry, so a run stuck in Transferring (a pod restart mid-upload) must be
    // releasable. Any blob this run actually wrote is swept first — never a blob owned by another
    // run — so the freed pair does not collide with a same-named retry.
    public async Task<EgressAbandonResult> AbandonAsync(Guid runId, CancellationToken ct)
    {
        var run = await repository.GetRunAsync(runId, ct);
        if (run is null) return new EgressAbandonResult.NotFound();
        if (run.Status == EgressRunStatus.Transferred) return new EgressAbandonResult.AlreadyTransferred();

        // S8 / R1: the write must happen — and its rows-affected be read — before any blob is
        // swept, not after. A concurrent TransferAsync can flip Transferring -> Transferred
        // between the read above and this write; sweeping first (the earlier ordering) would then
        // delete the files that transfer had just uploaded, while this call still went on to lose
        // the race and report AlreadyTransferred — leaving the run Transferred, with a Succeeded
        // audit row, but no files in LDS. Writing first and branching on what it actually affected
        // is what tells us which case happened. The write itself also runs on CancellationToken.
        // None, not the caller's token: per M1, once state starts changing, a closed tab must not
        // be able to abandon this call half-way.
        var wasTransferring = run.Status == EgressRunStatus.Transferring;
        var rows = await repository.AbandonAsync(runId, CancellationToken.None);
        if (rows == 0) return new EgressAbandonResult.AlreadyTransferred();

        var removed = new List<string>();
        if (wasTransferring)
        {
            foreach (var output in run.Outputs)
            {
                if (output.FileName is null) continue;
                if (await blobs.DeleteIfOwnedByRunAsync(output.FileName, runId, CancellationToken.None))
                    removed.Add(output.FileName);
            }
        }
        return new EgressAbandonResult.Abandoned(removed);
    }

    public async Task<byte[]> BuildFileAsync(Guid runId, EgressOutputType type, CancellationToken ct) =>
        (await BuildAsync(runId, await WindowTypeOfRunAsync(runId, ct), type, ct)).Bytes;

    public async Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)> GetPreviewAsync(Guid runId, EgressOutputType type, CancellationToken ct)
    {
        var windowType = await WindowTypeOfRunAsync(runId, ct);
        switch (type)
        {
            case EgressOutputType.NewLearners:
            {
                var columns = EgressColumnSets.NewLearnersFor(windowType);
                var rows = await repository.GetNewLearnersAsync(runId, ct);
                return (columns.Select(c => c.Header).ToList(), rows.Select(r => (IReadOnlyList<string>)columns.Select(c => c.Value(r)).ToList()).ToList());
            }
            case EgressOutputType.RemoveLearners:
            {
                var columns = EgressColumnSets.RemoveLearnersFor(windowType);
                var rows = await repository.GetRemoveLearnersAsync(runId, ct);
                return (columns.Select(c => c.Header).ToList(), rows.Select(r => (IReadOnlyList<string>)columns.Select(c => c.Value(r)).ToList()).ToList());
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No preview builder for this output type.");
        }
    }

    // The column set is the WINDOW's (spec v2.4 adds columns for KS4 and 16-19 only), so every
    // file, preview and download of a run resolves it the same way.
    private async Task<CheckingWindowType> WindowTypeOfRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await repository.GetRunAsync(runId, ct) ?? throw new InvalidOperationException($"Egress run {runId} no longer exists.");
        return await WindowTypeAsync(run.WindowId, runId, ct);
    }

    private async Task<CheckingWindowType> WindowTypeAsync(Guid windowId, Guid runId, CancellationToken ct)
    {
        var window = await windows.GetByIdAsync(windowId, ct)
            ?? throw new InvalidOperationException($"Checking window {windowId} for egress run {runId} no longer exists.");
        return window.CheckingWindowType;
    }

    private async Task<(byte[] Bytes, int Records)> BuildAsync(Guid runId, CheckingWindowType windowType, EgressOutputType type, CancellationToken ct)
    {
        switch (type)
        {
            case EgressOutputType.NewLearners:
            {
                var rows = await repository.GetNewLearnersAsync(runId, ct);
                return (EgressCsvWriter.Write(EgressColumnSets.NewLearnersFor(windowType), rows), rows.Count);
            }
            case EgressOutputType.RemoveLearners:
            {
                var rows = await repository.GetRemoveLearnersAsync(runId, ct);
                return (EgressCsvWriter.Write(EgressColumnSets.RemoveLearnersFor(windowType), rows), rows.Count);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No file builder for this output type.");
        }
    }
}
