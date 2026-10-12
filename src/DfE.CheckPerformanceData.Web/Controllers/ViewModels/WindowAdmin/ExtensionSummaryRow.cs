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

    /// <summary>From <c>ICheckingExerciseService.IsOpen</c> for this extension. Edit and Close early only while true.</summary>
    public bool IsOpen { get; init; }

    /// <summary>From <c>ICheckingExerciseService.HasClosed</c> for this extension. Send requests only while true.</summary>
    public bool HasClosed { get; init; }

    /// <summary>Its requests have been swept once, by an admin or by the automatic hand-over.</summary>
    public bool RequestsSent { get; init; }

    // Invariant culture: "/" in a custom format is the culture's date separator.
    public string EndDateText => EndDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private string BaseLink => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{Id}";
    public string EditLink => $"{BaseLink}/edit";
    public string CloseLink => $"{BaseLink}/close";
    public string SendRequestsLink => $"{BaseLink}/send-requests";
}
