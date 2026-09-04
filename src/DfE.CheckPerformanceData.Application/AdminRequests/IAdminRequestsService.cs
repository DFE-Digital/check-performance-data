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

    // Quick-and-dirty test hook: rebuild a RequestDocument for every SubmittedUnCommitted
    // request in the current open window(s) and drop each onto the Zendesk queue. Returns
    // the number of documents enqueued.
    //
    // Currently unreachable from the UI: the "Process Close Window" button is disabled and
    // AdminRequestsController exposes no action that calls this. Kept, with its tests, because
    // the close-window story will re-enable it — do not delete it as dead code.
    Task<int> ProcessCloseWindowEvent(CancellationToken cancellationToken);
}
