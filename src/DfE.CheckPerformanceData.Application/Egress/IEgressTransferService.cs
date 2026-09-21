using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public abstract record EgressTransferResult
{
    public sealed record Transferred(IReadOnlyList<(EgressOutputType OutputType, string FileName, int Records)> Files, DateTime TransferredAtUtc) : EgressTransferResult;
    /// <summary>Nit: carries the specific output type the retry lost, rather than the caller
    /// having to guess it via string surgery on a description built for a different type.</summary>
    public sealed record Refused(EgressOutputType OutputType, EgressBlocker Blocker) : EgressTransferResult;
    public sealed record Failed(string Reason) : EgressTransferResult;
    public sealed record NotTransferable(EgressRunStatus Status) : EgressTransferResult;
    /// <summary>M3: every output's saved row count is zero — nothing to send, so refused before any upload.</summary>
    public sealed record NothingToTransfer : EgressTransferResult;
    /// <summary>Nit: a missing run is its own outcome, not a guess dressed as NotTransferable(Abandoned).</summary>
    public sealed record NotFound : EgressTransferResult;
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
    /// <summary>Nit: the preview table built from the saved rows and EgressColumnSets directly,
    /// not by splitting the generated CSV text (which shifts columns for any quoted value).</summary>
    Task<(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)> GetPreviewAsync(Guid runId, EgressOutputType type, CancellationToken ct);
    Task<EgressAbandonResult> AbandonAsync(Guid runId, CancellationToken ct);
}
