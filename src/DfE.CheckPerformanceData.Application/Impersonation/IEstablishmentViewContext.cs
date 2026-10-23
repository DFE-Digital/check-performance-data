namespace DfE.CheckPerformanceData.Application.Impersonation;

/// <summary>Read authority only. Submissions must use the original ICurrentUserService.</summary>
public interface IEstablishmentViewContext
{
    bool IsImpersonating { get; }
    string OrganisationLaestab { get; }
    string OrganisationUrn { get; }
    int? LowestAge { get; }
    int? HighestAge { get; }
}

public sealed record EstablishmentSelection(string Laestab, string Urn, int LowestAge, int HighestAge);

public sealed record ImpersonationSessionState(string Generation, EstablishmentSelection? Selection);

public interface IImpersonationSessionStore
{
    Task<ImpersonationSessionState?> GetAsync(string binding, CancellationToken ct);
    Task SetAsync(string binding, ImpersonationSessionState state, CancellationToken ct);
}

public interface IImpersonationSessionLock
{
    Task<IAsyncDisposable> AcquireAsync(string binding, CancellationToken ct);
}
