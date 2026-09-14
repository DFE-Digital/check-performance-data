using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public abstract record EgressTransferResult
{
    public sealed record Transferred(IReadOnlyList<(EgressOutputType OutputType, string FileName, int Records)> Files, DateTime TransferredAtUtc) : EgressTransferResult;
    public sealed record Refused(EgressBlocker Blocker) : EgressTransferResult;
    public sealed record Failed(string Reason) : EgressTransferResult;
    public sealed record NotTransferable(EgressRunStatus Status) : EgressTransferResult;
    /// <summary>M3: every output's saved row count is zero — nothing to send, so refused before any upload.</summary>
    public sealed record NothingToTransfer : EgressTransferResult;
}

/// <summary>M1: Abandon needs blob access (to sweep a Transferring run's own files), which is why
/// it lives beside Transfer rather than on <see cref="IEgressRunService"/>.</summary>
public abstract record EgressAbandonResult
{
    public sealed record Abandoned(IReadOnlyList<string> RemovedFiles) : EgressAbandonResult;
    public sealed record AlreadyTransferred : EgressAbandonResult;
    public sealed record NotFound : EgressAbandonResult;
}

public interface IEgressTransferService
{
    Task<EgressTransferResult> TransferAsync(Guid runId, EgressActor actor, CancellationToken ct);
    Task<byte[]> BuildFileAsync(Guid runId, EgressOutputType type, CancellationToken ct);
    Task<EgressAbandonResult> AbandonAsync(Guid runId, CancellationToken ct);
}
