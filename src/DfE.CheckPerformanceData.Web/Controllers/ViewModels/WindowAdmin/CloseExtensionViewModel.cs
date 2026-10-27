using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#307773: the early-closure confirmation page of one extension, as
/// <see cref="CloseExerciseViewModel"/> is for an exercise — which extension, when it was due to
/// close, how many schools it holds, and the name the admin has to type to go ahead.
/// </summary>
public sealed class CloseExtensionViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required Guid ExtensionId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The exercise's name, as on its summary card: several releases may share a kind.</summary>
    public required string ExerciseLabel { get; init; }

    /// <summary>The extension's end date as it stands — a local wall-clock value.</summary>
    public required DateTime ScheduledEnd { get; init; }

    public required int SchoolCount { get; init; }

    /// <summary>What the admin typed, shown back after a mismatch so it can be corrected.</summary>
    public string? ConfirmWindowName { get; init; }

    /// <summary>Why the extension was not closed. Null on a first visit.</summary>
    public string? Error { get; init; }

    public bool HasError => Error is not null;

    // Invariant culture: "/" in a custom format is the culture's date separator.
    public string ScheduledEndDate => ScheduledEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string ScheduledEndTime => ScheduledEnd.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string Schools => SchoolCount == 1 ? "1 school" : $"{SchoolCount} schools";

    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{ExtensionId}/close";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
