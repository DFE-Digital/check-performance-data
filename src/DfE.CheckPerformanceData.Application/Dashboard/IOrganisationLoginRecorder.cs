using System.Security.Claims;

namespace DfE.CheckPerformanceData.Application.Dashboard;

public interface IOrganisationLoginRecorder
{
    /// <summary>
    /// Records one successful sign-in from the identity produced by claims enrichment.
    /// Silently skips (with a warning log) when the organisation claims cannot identify a
    /// school — callers rely on this never throwing for bad claim data.
    /// </summary>
    Task RecordLoginAsync(string userId, ClaimsIdentity enrichedIdentity, CancellationToken cancellationToken = default);
}
