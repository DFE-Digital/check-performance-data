using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

public interface IAdminRequestsService
{
    /// <summary>
    /// The change requests for one checking window, optionally narrowed to a single checking
    /// exercise, together with what the page needs to render its filter. Null when no window has
    /// that id — the caller reaches this from a URL segment, so a stale link must not 500.
    /// </summary>
    Task<WindowRequestsResult?> GetForWindowAsync(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken);
}
