using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public sealed record EgressActor(Guid UserId, string DisplayName, string? Email);

public abstract record EgressStartResult
{
    public sealed record Started(Guid RunId) : EgressStartResult;
    public sealed record Refused(IReadOnlyList<(EgressOutputType OutputType, EgressBlocker Blocker)> Blockers) : EgressStartResult;
    public sealed record WindowNotFound : EgressStartResult;
    public sealed record PullFailed(string Reason) : EgressStartResult;
}

public interface IEgressRunService
{
    Task<EgressStartResult> StartAsync(Guid windowId, IReadOnlyList<EgressOutputType> outputTypes, EgressActor actor, CancellationToken ct);
    Task<EgressRunDto?> GetAsync(Guid runId, CancellationToken ct);
    Task<IReadOnlyList<EgressRunListItem>> ListAsync(CancellationToken ct);
}
