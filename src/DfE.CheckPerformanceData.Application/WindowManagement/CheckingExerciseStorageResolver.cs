using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface ICheckingExerciseStorageResolver
{
    Task<CheckingExerciseDto?> ResolveAsync(Guid windowId, CheckingExerciseType type, CancellationToken cancellationToken = default);
}

// Adapter for legacy request journeys that still address a Window and activity type.
// Ambiguous visible releases fail closed; they must never silently select the first dataset.
public sealed class CheckingExerciseStorageResolver(IWindowRepository windows, TimeProvider clock) : ICheckingExerciseStorageResolver
{
    public async Task<CheckingExerciseDto?> ResolveAsync(Guid windowId, CheckingExerciseType type, CancellationToken cancellationToken = default)
    {
        var window = await windows.GetByIdAsync(windowId, cancellationToken);
        var now = clock.GetLocalNow().DateTime;
        var candidates = window?.Exercises.Where(e => e.ExerciseType == type
            && (e.DataType is null || e.DataType == CheckingExerciseBlobPaths.DefaultDataType(type)) && (e.TabName is null ||
            (e.IsEnabled && (e.VisibleFrom is null || e.VisibleFrom <= now) && (e.VisibleUntil is null || e.VisibleUntil > now)))).Take(2).ToList();
        return candidates is { Count: 1 } ? candidates[0] : null;
    }
}
