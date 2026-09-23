using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>Window + activity type -> the one exercise row whose storage that journey should use.</summary>
public interface ICheckingExerciseStorageResolver
{
    Task<CheckingExerciseDto?> ResolveAsync(Guid windowId, CheckingExerciseType type,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The adapter for the journeys that still address a window and an activity type — pupil data and
/// results enquiry. Since #466 a window may hold several releases of one activity, so the pair no
/// longer identifies a row on its own.
/// </summary>
/// <remarks>
/// It fails closed. An ambiguous window resolves to null and the journey finds no data, which is
/// visible and correctable. Taking the first match instead would serve one release's data under
/// another release's journey, which is neither.
/// </remarks>
public sealed class CheckingExerciseStorageResolver(IWindowRepository windows, TimeProvider clock)
    : ICheckingExerciseStorageResolver
{
    public async Task<CheckingExerciseDto?> ResolveAsync(Guid windowId, CheckingExerciseType type,
        CancellationToken cancellationToken = default)
    {
        var window = await windows.GetByIdAsync(windowId, cancellationToken);
        var now = clock.GetLocalNow().DateTime;

        // A row with no tab name was configured before #466. It has no visibility dates to honour,
        // so the visibility rules are not applied to it — only to rows that opted into them.
        var candidates = window?.Exercises.Where(e => e.ExerciseType == type
            && (e.TabName is null
                || (e.IsEnabled
                    && (e.VisibleFrom is null || e.VisibleFrom <= now)
                    && (e.VisibleUntil is null || e.VisibleUntil > now)))).ToList();

        // A display-only release never owns a journey's storage while an interactive one exists.
        var interactive = candidates?.Where(e => !e.DisplayOnly).ToList();
        if (interactive is { Count: > 0 }) candidates = interactive;

        return candidates is { Count: 1 } ? candidates[0] : null;
    }
}
