namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#302158: the one statement of when an exercise is due for automatic hand-over.
/// </summary>
/// <remarks>
/// Due from <c>end + delay</c> (inclusive) until <c>end + delay + catch-up window</c> (exclusive).
/// There is no record that a hand-over ran: the sweep only ever picks up requests that have not
/// been sent and drafts that are still live, so running it again finds nothing, and the job simply
/// runs it on every tick inside the window. After the window the exercise is never looked at
/// again — which is also what leaves alone every exercise that closed long before this shipped.
///
/// Both dates are local wall-clock values, like every exercise date; the caller supplies "now"
/// from <c>TimeProvider.GetLocalNow()</c>, the clock <see cref="ICheckingExerciseService"/> reads.
/// </remarks>
public static class ExerciseHandOverSchedule
{
    public static bool IsDue(DateTime exerciseEnd, DateTime now, ExerciseHandOverSettings settings)
    {
        var dueFrom = exerciseEnd + settings.EffectiveDelayAfterEnd;
        return now >= dueFrom && now < dueFrom + settings.EffectiveCatchUpWindow;
    }
}
