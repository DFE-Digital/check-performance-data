using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AdminRequests;

public sealed class AdminRequestsService(
    IAdminRequestsRepository repository,
    IWindowService windowService,
    IZendeskTicketLinks ticketLinks) : IAdminRequestsService
{
    public async Task<WindowRequestsResult?> GetForWindowAsync(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(windowId, cancellationToken);
        if (window is null)
            return null;

        // A display-only exercise has no kind to filter by, so it is not offered as a filter option
        // (it has no ChangeRequest rows stamped with a kind either).
        var exercises = window.Exercises
            .InTabOrder()
            .Select(e => e.ExerciseType)
            .OfType<CheckingExerciseType>()
            .ToList();

        // A filter naming an exercise this window does not run is dropped rather than honoured.
        // Honouring it would return an empty table that looks like "no requests" when the truthful
        // answer is "this window has no such exercise", and the option was never offered.
        CheckingExerciseType? selected =
            exercise is { } asked && exercises.Contains(asked) ? asked : null;

        var rows = await repository.GetForWindowAsync(windowId, selected, cancellationToken);

        return new WindowRequestsResult
        {
            WindowId = windowId,
            WindowTitle = window.Title,
            Exercises = exercises,
            SelectedExercise = selected,
            Rows = [.. rows.Select(r => r with { ZendeskTicketUrl = ticketLinks.TicketUrl(r.CrmId) })]
        };
    }
}
