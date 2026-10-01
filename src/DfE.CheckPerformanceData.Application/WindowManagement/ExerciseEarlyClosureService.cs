using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="IExerciseEarlyClosureService"/>
/// <remarks>
/// The clock is read here only to STAMP the close. Whether the exercise is open is asked of
/// <see cref="ICheckingExerciseService"/>, which stays the one place that compares an exercise's
/// dates with now. Both read the same <see cref="TimeProvider"/> the same way (local wall clock),
/// because exercise dates are stored as local wall-clock values.
/// </remarks>
public sealed class ExerciseEarlyClosureService(
    IWindowRepository windows,
    ICheckingExerciseService checkingExercises,
    TimeProvider timeProvider) : IExerciseEarlyClosureService
{
    public async Task<EarlyClosureResult> CloseEarlyAsync(
        Guid windowId, CheckingExerciseType exercise, EarlyClosureActor actor, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windows.GetByIdAsync(windowId, cancellationToken);
        CheckingExerciseDto? row = window?.FindExercise(exercise);
        if (window is null || row is null)
        {
            return EarlyClosureResult.NotFound;
        }

        if (!checkingExercises.IsOpen(window.Exercises, exercise))
        {
            return EarlyClosureResult.NotOpen;
        }

        DateTime closedAt = ToTheSecond(timeProvider.GetLocalNow().DateTime);

        bool written = await windows.CloseExerciseEarlyAsync(new ExerciseEarlyClosure
        {
            WindowId = windowId,
            Exercise = exercise,
            ScheduledEnd = row.EndDate,
            // One second before the close, not the close itself: an exercise's last instant is
            // open (EndDate >= now), so ending it AT now would leave it open for that instant.
            NewEndDate = closedAt.AddSeconds(-1),
            ClosedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            UserId = actor.UserId,
            ClosedByName = actor.DisplayName
        }, cancellationToken);

        // False means the end date changed between the read above and the write: someone else
        // closed it or edited its dates. Either way this press closed nothing.
        return written ? EarlyClosureResult.Closed(closedAt, row.EndDate) : EarlyClosureResult.NotOpen;
    }

    // Exercise dates are shown and edited to the minute; sub-second precision would only make the
    // stored value differ from anything a person could read back.
    private static DateTime ToTheSecond(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified);
}
