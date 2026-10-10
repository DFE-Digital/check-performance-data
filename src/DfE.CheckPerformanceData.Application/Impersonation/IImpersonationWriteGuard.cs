using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.Impersonation;

public interface IImpersonationWriteGuard
{
    Task EnsureCanWriteAsync(Guid windowId, RequestState? journey = null);
}

public sealed class ImpersonationWriteDeniedException() : InvalidOperationException("Changes cannot be made in read-only impersonation or with a stale establishment context.");
