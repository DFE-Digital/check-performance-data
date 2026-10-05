using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The "are you sure" page before one checking exercise is deleted.</summary>
public sealed class DeleteExerciseViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string WindowTitle { get; init; }
    public required string ExerciseName { get; init; }
    public required string KindLabel { get; init; }

    /// <summary>Null for a data share, which has no kind and so no Close.</summary>
    public required CheckingExerciseType? ExerciseType { get; init; }

    /// <summary>Schools can see this exercise now, and lose its tab the moment it is deleted.</summary>
    public required bool IsVisibleToSchools { get; init; }

    /// <summary>The exercise's files are under its own id, so they are deleted with it.</summary>
    public required bool UsesExerciseStorage { get; init; }

    public required int DatasetCount { get; init; }
    public required int ReleaseCount { get; init; }

    /// <summary>No other exercise is left, so schools will not see the window at all.</summary>
    public required bool IsLastExercise { get; init; }

    /// <summary>Other exercises that replace this one. They stay, and no longer replace anything.</summary>
    public IReadOnlyList<string> ReplacedBy { get; init; } = [];

    public required DeleteExercisePreview Preview { get; init; }

    /// <summary>
    /// The admin ticks this to say they know the exercise's change requests go too. Only asked when
    /// there are requests, as on the delete window page.
    /// </summary>
    public bool ConfirmRequestsDeleted { get; init; }

    public string CancelLink => $"/admin/windows/summary/{WindowId}";

    /// <summary>Open to schools now, so Close is the way to send its requests (AB#301022).</summary>
    public bool IsOpen { get; init; }

    /// <summary>Past its end date, so "Send requests for processing" is the way (AB#301022).</summary>
    public bool HasClosed { get; init; }

    /// <summary>
    /// Closing sends the exercise's submitted requests for processing, so the page offers it before
    /// they are lost: Close while it is open, "Send requests for processing" once it has closed,
    /// nothing before it starts. A data share has no kind and so no requests.
    /// </summary>
    public string? CloseLink => ExerciseType is null ? null
        : IsOpen ? $"/admin/windows/{WindowId}/exercises/{ExerciseId}/close"
        : HasClosed ? $"/admin/windows/{WindowId}/exercises/{ExerciseId}/send-requests"
        : null;
    public string FormAction => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/delete";
}
