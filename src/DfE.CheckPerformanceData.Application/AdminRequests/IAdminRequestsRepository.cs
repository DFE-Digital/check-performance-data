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

    /// <summary>
    /// Replay projection of the SubmittedUnCommitted rows belonging to ONE checking exercise of one
    /// window, used to rebuild RequestDocuments when that exercise is closed.
    /// </summary>
    /// <remarks>
    /// Scoped by the exercise's row id, resolved from the window id + type. A row whose
    /// CheckingExerciseId is null therefore matches nothing and is left alone — deliberate: such a
    /// row was orphaned (the FK is onDelete: SetNull) or belongs to a window that never ran the
    /// mapped exercise, so committing it under this exercise would be a guess. It stays
    /// SubmittedUnCommitted and visible on the Requests page instead.
    ///
    /// There is no date parameter. Closing is an admin decision, not a consequence of the clock.
    /// </remarks>
    Task<IReadOnlyList<ReplayRequestRow>> GetRequestsForExerciseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken);

    // Sets a single ChangeRequest row's status by its Id.
    Task SetStatusAsync(Guid changeRequestId, RequestStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every InProgress / ReadyToSubmit draft belonging to one checking exercise to
    /// NotSubmitted. Returns the number of rows changed. Same exercise scoping as
    /// <see cref="GetRequestsForExerciseAsync"/>.
    /// </summary>
    Task<int> MarkDraftsNotSubmittedForExerciseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken);

    /// <summary>
    /// How many drafts <see cref="MarkDraftsNotSubmittedForExerciseAsync"/> would move, without
    /// moving them. Feeds the close confirmation page.
    /// </summary>
    Task<int> CountDraftsForExerciseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken);
}
