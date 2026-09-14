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
            return await FailAsync(runId, actor, "Egress storage is not configured for this environment (ConnectionStrings:EgressStorage).", [], ct);

        if (!await repository.TrySetStatusAsync(runId, run.Status, EgressRunStatus.Transferring, ct))
            return new EgressTransferResult.NotTransferable(EgressRunStatus.Transferring);

        var files = new Dictionary<EgressOutputType, (string FileName, int Records, string Sha256)>();
        var uploaded = new List<string>();
        try
        {
            foreach (var output in run.Outputs)
            {
                var (bytes, records) = await BuildAsync(runId, output.OutputType, ct);
                var fileName = output.FileName ?? throw new InvalidOperationException($"Run {runId} has no file name for {output.OutputType}.");
                var sha = Convert.ToHexString(SHA256.HashData(bytes));
                await blobs.UploadAsync(fileName, bytes, sha, runId, ct);
                uploaded.Add(fileName);
                files[output.OutputType] = (fileName, records, sha);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Egress run {RunId} transfer failed after {Uploaded} file(s); compensating", runId, uploaded.Count);
            return await FailAsync(runId, actor, ex.Message, uploaded, ct);
        }

        var at = DateTime.UtcNow;
        await repository.MarkTransferredAsync(runId, new EgressTransferAudit(actor.UserId.ToString(), actor.DisplayName, blobs.TargetDescription, files), at, ct);
        return new EgressTransferResult.Transferred(files.Select(f => (f.Key, f.Value.FileName, f.Value.Records)).ToList(), at);
    }

    private async Task<EgressTransferResult> FailAsync(Guid runId, EgressActor actor, string reason, IReadOnlyList<string> uploaded, CancellationToken ct)
    {
        foreach (var name in uploaded)
        {
            try { await blobs.DeleteIfExistsAsync(name, CancellationToken.None); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Egress run {RunId}: could not delete {File} while compensating a failed transfer", runId, name);
                reason += $" Could not remove {name} from the target container — remove it by hand before retrying.";
            }
        }
        await repository.MarkTransferFailedAsync(runId, reason, actor.UserId.ToString(), CancellationToken.None);
        return new EgressTransferResult.Failed(reason);
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
