using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The close confirmation page: what is about to be swept, and for which exercise.</summary>
public sealed class CloseExerciseViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required CheckingExerciseType ExerciseType { get; init; }
    public required string ExerciseLabel { get; init; }

    public required int RequestsToClose { get; init; }
    public required int DraftsToCancel { get; init; }

    /// <summary>
    /// Closing an already-swept exercise is harmless, so the page renders normally and says there
    /// is nothing to do rather than treating it as an error.
    /// </summary>
    public bool HasNothingToDo => RequestsToClose == 0 && DraftsToCancel == 0;

    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
