namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// The "send requests for processing" confirmation page (AB#301022): what the sweep is about to
/// touch, for which closed exercise.
/// </summary>
public sealed class SendRequestsViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The exercise's name, as on its summary card: several releases may share a kind.</summary>
    public required string ExerciseLabel { get; init; }

    public required int RequestsToSend { get; init; }
    public required int DraftsToCancel { get; init; }

    /// <summary>
    /// Sending for an already-swept exercise is harmless, so the page renders normally and says
    /// there is nothing to do rather than treating it as an error.
    /// </summary>
    public bool HasNothingToDo => RequestsToSend == 0 && DraftsToCancel == 0;

    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/send-requests";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
