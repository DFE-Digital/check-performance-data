using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

public sealed class AdminRequestsService(
    IAdminRequestsRepository repository,
    IWindowService windowService) : IAdminRequestsService
{
    public async Task<WindowRequestsResult?> GetForWindowAsync(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(windowId, cancellationToken);
        if (window is null)
            return null;

        var exercises = window.Exercises
            .OrderBy(e => e.SortOrder)
            .Select(e => e.ExerciseType)
            .ToList();

        // A filter naming an exercise this window does not run is dropped rather than honoured.
        // Honouring it would return an empty table that looks like "no requests" when the truthful
        // answer is "this window has no such exercise", and the option was never offered.
        CheckingExerciseType? selected =
            exercise is { } asked && exercises.Contains(asked) ? asked : null;

        return new WindowRequestsResult
        {
            WindowId = windowId,
            WindowTitle = window.Title,
            Exercises = exercises,
            SelectedExercise = selected,
            Rows = await repository.GetForWindowAsync(windowId, selected, cancellationToken)
        };
    }
}
