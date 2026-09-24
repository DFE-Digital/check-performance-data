using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The confirmation page before an earlier (or later) release of an exercise goes live.</summary>
public sealed class MakeReleaseLiveViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string WindowTitle { get; init; }
    public required string ExerciseName { get; init; }
    public required CheckingExerciseReleaseDto Release { get; init; }

    /// <summary>The release schools see now. Null when the exercise has none.</summary>
    public CheckingExerciseReleaseDto? LiveRelease { get; init; }

    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/releases/{Release.Id}/make-live";
    public string CancelLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/edit#releases";
}
