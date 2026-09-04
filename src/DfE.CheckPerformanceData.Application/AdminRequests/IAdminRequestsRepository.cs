using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

public interface IAdminRequestsRepository
{
    /// <summary>
    /// The change requests in one checking window, most recently submitted first. When
    /// <paramref name="exercise"/> is given, only the rows stamped with that exercise's row on this
    /// window are returned — a row whose CheckingExerciseId is null (written before the column
    /// existed on a window that never ran the exercise) is therefore excluded by any filter, which
    /// is the honest answer: nothing says it belongs to the exercise being asked about.
    /// </summary>
    Task<IReadOnlyList<AdminRequestRow>> GetForWindowAsync(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken);

    // Replay projection of the SubmittedUnCommitted rows in currently-open windows,
    // used to rebuild RequestDocuments for the manual "send to Zendesk" admin action.
    Task<IReadOnlyList<ReplayRequestRow>> GetRequestsForOpenWindowsAsync(
        DateTime now, CancellationToken cancellationToken);

    // Sets a single ChangeRequest row's status by its Id.
    Task SetStatusAsync(Guid changeRequestId, RequestStatus status, CancellationToken cancellationToken);

    // Moves every InProgress / ReadyToSubmit draft in the currently-open windows to
    // NotSubmitted. Returns the number of rows changed.
    Task<int> MarkDraftsNotSubmittedForOpenWindowsAsync(DateTime now, CancellationToken cancellationToken);
}
