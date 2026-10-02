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

/// <summary>An exercise that is due for automatic hand-over. The end date is a local wall-clock value.</summary>
public sealed record DueExercise(Guid WindowId, string WindowTitle, CheckingExerciseType Exercise, DateTime ExerciseEnd);

/// <summary>What one automatic hand-over did. Both counts are zero when it failed or found nothing.</summary>
public sealed record AutomaticHandOverOutcome(
    Guid WindowId, CheckingExerciseType Exercise, int RequestsSent, int DraftsCancelled, bool Failed);
