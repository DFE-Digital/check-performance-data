using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#307773: one extension of an exercise on the window summary card — its end, how many schools
/// it names, and which of its actions the card may offer.
/// </summary>
public sealed class ExtensionSummaryRow
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required Guid Id { get; init; }
    public required DateTime EndDate { get; init; }
    public required int SchoolCount { get; init; }

    /// <summary>The exercise's own end, quoted while the extension waits for it.</summary>
    public required DateTime ExerciseEndDate { get; init; }

    /// <summary>
    /// From <c>ICheckingExerciseService.IsOpen(extension)</c>: the extension's own end is ahead.
    /// The tag reads Open, and Edit and Close early are offered only while true.
    /// </summary>
    public bool IsOpen { get; init; }

    /// <summary>
    /// From <c>ICheckingExerciseService.HasClosed(exercise, extension)</c>: the effective end,
    /// the later of the extension's and the exercise's, has passed. Send requests only while true.
    /// </summary>
    public bool HasClosed { get; init; }

    /// <summary>Its requests have been swept once, by an admin or by the automatic hand-over.</summary>
    public bool RequestsSent { get; init; }

    /// <summary>
    /// Closed by its own end (closed early, or overtaken by a later exercise end) while the
    /// exercise still runs: its schools keep the exercise's time, so nothing can be sent yet.
    /// </summary>
    public bool WaitsForExercise => !IsOpen && !HasClosed;

    // Invariant culture: "/" in a custom format is the culture's date separator.
    public string EndDateText => EndDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string ExerciseEndDateText => ExerciseEndDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private string BaseLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{Id}";
    public string EditLink => $"{BaseLink}/edit";
    public string CloseLink => $"{BaseLink}/close";
    public string SendRequestsLink => $"{BaseLink}/send-requests";
}
