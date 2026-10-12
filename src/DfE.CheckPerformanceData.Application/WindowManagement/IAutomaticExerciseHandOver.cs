using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#302158: hands a checking exercise's requests over for processing without an admin, two
/// hours after the exercise ends. "Hand over" is exactly what <see cref="ICloseExerciseService"/>
/// does — submitted requests onto the Zendesk queue, leftover drafts cancelled — and this calls
/// it unchanged.
/// </summary>
/// <remarks>
/// Two methods rather than one "run everything", so the caller can give each hand-over a
/// dependency scope (and so a DbContext) of its own: a save that fails in one exercise's sweep
/// must not leave half-tracked entities behind for the next exercise's.
///
/// This is about an EXERCISE's end, never the window's: a window whose pupil-data checking has
/// ended is usually still open for results enquiries.
/// </remarks>
public interface IAutomaticExerciseHandOver
{
    /// <summary>Every exercise, in every window, that is due right now (see <see cref="ExerciseHandOverSchedule"/>).</summary>
    Task<IReadOnlyList<DueExercise>> FindDueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the hand-over for one exercise and, if it sent or cancelled anything, records it in
    /// the audit log. A failure is logged and reported in the outcome, never thrown — the next
    /// tick tries again — except cancellation of the sweep, which is. The audit row is written
    /// even if the token is cancelled, because the hand-over has already happened and its record
    /// must not be abandoned because the host is stopping.
    /// </summary>
    Task<AutomaticHandOverOutcome> HandOverAsync(DueExercise due, CancellationToken cancellationToken);
}

/// <summary>
/// An exercise, or one of its extensions (#567), that is due for automatic hand-over. There is one
/// item per exercise and one per extension, each due two hours after its end;
/// <paramref name="ExerciseEnd"/> is that end. For an extension item it is the effective end,
/// <c>max(exercise end, extension end)</c>, so an extension closed early waits for the exercise. The end date
/// is a local wall-clock value. The sweep is keyed by <paramref name="ExerciseId"/>, because a window
/// may hold several releases of one kind (#466); the kind is carried for the log and the audit row.
/// When <paramref name="ExtensionId"/> is set, the sweep is keyed by it and touches only that
/// extension's schools, leaving the exercise's other schools alone.
/// </summary>
public sealed record DueExercise(
    Guid WindowId, string WindowTitle, Guid ExerciseId, CheckingExerciseType Exercise, DateTime ExerciseEnd,
    Guid? ExtensionId = null);

/// <summary>What one automatic hand-over did. Both counts are zero when it failed or found nothing.</summary>
public sealed record AutomaticHandOverOutcome(
    Guid WindowId, Guid ExerciseId, CheckingExerciseType Exercise, int RequestsSent, int DraftsCancelled, bool Failed,
    Guid? ExtensionId = null);
