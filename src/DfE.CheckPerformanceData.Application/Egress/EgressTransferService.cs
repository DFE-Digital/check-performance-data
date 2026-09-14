using System.Security.Cryptography;
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
public sealed class EgressTransferService(IEgressRunRepository repository, IEgressBlobClient blobs, ILogger<EgressTransferService> logger) : IEgressTransferService
{
    public async Task<EgressTransferResult> TransferAsync(Guid runId, EgressActor actor, CancellationToken ct)
    {
        var run = await repository.GetRunAsync(runId, ct);
        if (run is null) return new EgressTransferResult.NotTransferable(EgressRunStatus.Abandoned);
        if (run.Status is not (EgressRunStatus.Preprocessed or EgressRunStatus.TransferFailed))
            return new EgressTransferResult.NotTransferable(run.Status);

        if (run.Status == EgressRunStatus.TransferFailed)
        {
            var blocker = await repository.TryReactivateAsync(runId, ct);
            if (blocker is not null) return new EgressTransferResult.Refused(blocker);
        }

        if (!blobs.IsConfigured)
            return await FailAsync(runId, actor, "Egress storage is not configured for this environment (ConnectionStrings:EgressStorage).", [], null, ct);

        if (!await repository.TrySetStatusAsync(runId, run.Status, EgressRunStatus.Transferring, ct))
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
                var (bytes, records) = await BuildAsync(runId, output.OutputType, operationCt);
                var fileName = output.FileName ?? throw new InvalidOperationException($"Run {runId} has no file name for {output.OutputType}.");
                inFlight = fileName;
                var sha = Convert.ToHexString(SHA256.HashData(bytes));
                await blobs.UploadAsync(fileName, bytes, sha, runId, operationCt);
                inFlight = null;
                uploaded.Add(fileName);
                files[output.OutputType] = (fileName, records, sha);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Egress run {RunId} transfer failed after {Uploaded} file(s); compensating", runId, uploaded.Count);
            return await FailAsync(runId, actor, ex.Message, uploaded, inFlight, operationCt);
        }

        DateTime at;
        try
        {
            at = DateTime.UtcNow;
            await repository.MarkTransferredAsync(runId, new EgressTransferAudit(actor.UserId.ToString(), actor.DisplayName, blobs.TargetDescription, files), at, operationCt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Egress run {RunId}: MarkTransferredAsync failed after every file was uploaded; compensating", runId);
            return await FailAsync(runId, actor, ex.Message, uploaded, null, operationCt);
        }
        return new EgressTransferResult.Transferred(files.Select(f => (f.Key, f.Value.FileName, f.Value.Records)).ToList(), at);
    }

    // possiblyOrphaned (S3): the file that was mid-upload when the exception was thrown, if any —
    // its PUT may have actually landed server-side with the response lost to the client, so it is
    // worth a metadata-checked delete attempt distinct from `uploaded` (which this call definitely
    // wrote, since a create-only PUT that returned success cannot belong to another run).
    private async Task<EgressTransferResult> FailAsync(Guid runId, EgressActor actor, string reason, IReadOnlyList<string> uploaded, string? possiblyOrphaned, CancellationToken ct)
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
        await repository.MarkTransferFailedAsync(runId, reason, actor.UserId.ToString(), ct);
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

        var removed = new List<string>();
        if (run.Status == EgressRunStatus.Transferring)
        {
            foreach (var output in run.Outputs)
            {
                if (output.FileName is null) continue;
                if (await blobs.DeleteIfOwnedByRunAsync(output.FileName, runId, CancellationToken.None))
                    removed.Add(output.FileName);
            }
        }
        await repository.AbandonAsync(runId, ct);
        return new EgressAbandonResult.Abandoned(removed);
    }

    public async Task<byte[]> BuildFileAsync(Guid runId, EgressOutputType type, CancellationToken ct) => (await BuildAsync(runId, type, ct)).Bytes;

    private async Task<(byte[] Bytes, int Records)> BuildAsync(Guid runId, EgressOutputType type, CancellationToken ct)
    {
        switch (type)
        {
            case EgressOutputType.NewLearners:
            {
                var rows = await repository.GetNewLearnersAsync(runId, ct);
                return (EgressCsvWriter.Write(EgressColumnSets.NewLearners, rows), rows.Count);
            }
            case EgressOutputType.RemoveLearners:
            {
                var rows = await repository.GetRemoveLearnersAsync(runId, ct);
                return (EgressCsvWriter.Write(EgressColumnSets.RemoveLearners, rows), rows.Count);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No file builder for this output type.");
        }
    }
}
