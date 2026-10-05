namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Closes one checking exercise: sends that exercise's decided amendments to the Zendesk queue,
/// and cancels its leftover drafts. Undecided amendments are left for a later run (#536).
/// </summary>
/// <remarks>
/// Replaces <c>AdminRequestsService.ProcessCloseWindowEvent</c>, which swept every OPEN window at
/// once and so could not be driven from a per-window admin page. Two rules hold here:
/// <list type="bullet">
/// <item>The sweep is scoped to one window AND one exercise row, named by its id. A window may hold
/// several releases of one kind (#466), so a type would not say which. A press cannot reach another
/// window's rows, another exercise's rows, or a row belonging to no exercise.</item>
/// <item>It ignores the exercise's dates. There is deliberately no <c>TimeProvider</c> — an admin
/// may close an exercise whose end date has not passed.</item>
/// </list>
/// </remarks>
public interface ICloseExerciseService
{
    /// <summary>
    /// What a close would do, without doing it. Counted over the same rows <see cref="CloseAsync"/>
    /// would sweep, so the confirmation page and the sweep cannot describe different sets. This is
    /// a count, not a reservation: rows may change between the preview and the press, so the result
    /// banner reports what the close actually did rather than what this predicted.
    /// </summary>
    Task<CloseExercisePreview> PreviewAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>
    /// Performs the close. Irreversible: it dispatches to an external system and marks the sent
    /// rows TicketQueued.
    /// </summary>
    Task<CloseExerciseResult> CloseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken);
}

/// <summary>Counts only — nothing is written to produce this.</summary>
public sealed record CloseExercisePreview
{
    public required int RequestsToClose { get; init; }
    public required int DraftsToCancel { get; init; }

    /// <summary>Submitted amendments the Rules Engine has not decided yet; the sweep leaves them.</summary>
    public int RequestsWaiting { get; init; }

    /// <summary>Closing an already-swept exercise is harmless; the page says so rather than erroring.</summary>
    public bool HasNothingToDo => RequestsToClose == 0 && DraftsToCancel == 0 && RequestsWaiting == 0;
}

/// <summary>What the close actually did.</summary>
public sealed record CloseExerciseResult
{
    public required int Enqueued { get; init; }
    public required int DraftsCancelled { get; init; }

    /// <summary>Submitted amendments left for a later run because the Rules Engine had not decided them.</summary>
    public int Waiting { get; init; }
}
