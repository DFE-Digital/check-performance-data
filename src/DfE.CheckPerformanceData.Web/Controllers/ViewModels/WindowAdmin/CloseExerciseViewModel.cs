using System.Globalization;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// The early-closure confirmation page (AB#301022): which exercise of which window is about to be
/// closed, when it was due to close, and the name the admin has to type to go ahead.
/// </summary>
public sealed class CloseExerciseViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required CheckingExerciseType ExerciseType { get; init; }
    public required string ExerciseLabel { get; init; }

    /// <summary>The exercise's end date as it stands — a local wall-clock value.</summary>
    public required DateTime ScheduledEnd { get; init; }

    /// <summary>What the admin typed, shown back after a mismatch so it can be corrected.</summary>
    public string? ConfirmWindowName { get; init; }

    /// <summary>Why the exercise was not closed. Null on a first visit.</summary>
    public string? Error { get; init; }

    public bool HasError => Error is not null;

    // Invariant culture: "/" in a custom format is the culture's date separator, and the page must
    // read the same on a build agent as on the server.
    public string ScheduledEndDate => ScheduledEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string ScheduledEndTime => ScheduledEnd.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// What schools can no longer do once this exercise is closed. FLAGGED copy. No default: a new
    /// exercise type must be given its own sentence rather than borrow another's.
    /// </summary>
    public string Consequence => ExerciseType switch
    {
        CheckingExerciseType.PupilData =>
            "Closing it now stops schools submitting further amendment requests.",
        CheckingExerciseType.ResultsEnquiry =>
            "Closing it now stops schools reporting further issues with their results.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(ExerciseType), ExerciseType, "No early-closure wording for this checking exercise.")
    };

    public string PostUrl => $"/admin/windows/{WindowId}/{ExerciseType}/close";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
