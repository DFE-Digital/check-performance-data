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
    /// The submitted amendments of ONE checking exercise that the Rules Engine has decided and that
    /// have no ticket: what the close sweep sends to Zendesk (#536). A row the sweep has already
    /// queued is TicketQueued and is not returned, so the sweep can run again safely.
    /// </summary>
    /// <remarks>
    /// Scoped by the exercise's row id, resolved from the window id + type. A row whose
    /// CheckingExerciseId is null therefore matches nothing and is left alone — deliberate: such a
    /// row was orphaned (the FK is onDelete: SetNull) or belongs to a window that never ran the
    /// mapped exercise, so sending it under this exercise would be a guess.
    ///
    /// Amendments only. A results enquiry is queued at submit, and a ConfirmCorrect declaration
    /// never goes to Zendesk. There is no date parameter: closing is an admin decision.
    /// </remarks>
    Task<IReadOnlyList<ReplayRequestRow>> GetDecidedRequestsForExerciseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken);

    /// <summary>
    /// Submitted amendments of the same exercise that the Rules Engine has not decided yet. The
    /// sweep leaves them; a later run sends them.
    /// </summary>
    Task<int> CountWaitingRequestsForExerciseAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken);

    /// <summary>
    /// Decided → TicketQueued for one row. Returns false when the row was not Decided (another run
    /// queued it first), so the caller does not count it.
    /// </summary>
    Task<bool> MarkTicketQueuedAsync(Guid changeRequestId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction. The queue is in the same database,
    /// so the sweep marks a row TicketQueued and puts its message on the queue together, or not at all.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every InProgress / ReadyToSubmit draft belonging to one checking exercise to
    /// NotSubmitted. Returns the number of rows changed. Same exercise scoping as
    /// <see cref="GetDecidedRequestsForExerciseAsync"/>.
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
