using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public abstract record EgressTransferResult
{
    public sealed record Transferred(IReadOnlyList<(EgressOutputType OutputType, string FileName, int Records)> Files, DateTime TransferredAtUtc) : EgressTransferResult;
    public sealed record Refused(EgressBlocker Blocker) : EgressTransferResult;
    public sealed record Failed(string Reason) : EgressTransferResult;
    public sealed record NotTransferable(EgressRunStatus Status) : EgressTransferResult;
}

public interface IEgressTransferService
{
    Task<EgressTransferResult> TransferAsync(Guid runId, EgressActor actor, CancellationToken ct);
    Task<byte[]> BuildFileAsync(Guid runId, EgressOutputType type, CancellationToken ct);
}
