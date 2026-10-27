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
/// results enquiry. A window has at most one exercise of each kind (<see cref="OneExercisePerKind"/>),
/// and the journey uses it only while schools can see it (enabled, inside its visibility dates).
/// </summary>
/// <remarks>
/// <para>
/// A window saved before the one-per-kind rule may still hold two of a kind. If both are live the
/// answer is null and the journey finds no data, which is visible and correctable. Taking the
/// first instead would serve one exercise's data under the other's journey, which is neither.
/// </para>
/// <para>
/// The returned exercise carries no datasets or releases: callers need only its id, its storage
/// and its current release. One page asks several times (the results client, the late results
/// warning, the request), so the answer is kept for the rest of the request (the resolver is
/// scoped). It is not kept any longer, because a new release must reach schools at once.
/// </para>
/// </remarks>
public sealed class CheckingExerciseStorageResolver(IWindowRepository windows, TimeProvider clock)
    : ICheckingExerciseStorageResolver
{
    private readonly Dictionary<(Guid, CheckingExerciseType), CheckingExerciseDto?> _resolved = new();

    public async Task<CheckingExerciseDto?> ResolveAsync(Guid windowId, CheckingExerciseType type,
        CancellationToken cancellationToken = default)
    {
        if (_resolved.TryGetValue((windowId, type), out var known)) return known;

        var now = clock.GetLocalNow().DateTime;
        var live = (await windows.GetExercisesOfKindAsync(windowId, type, cancellationToken))
            .Where(e => e.IsLiveAt(now))
            .ToList();

        var resolved = live is [var only] ? only : null;
        _resolved[(windowId, type)] = resolved;
        return resolved;
    }
}
