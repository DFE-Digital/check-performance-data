using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#307773: the "send requests for processing" confirmation page of one closed extension, as
/// <see cref="SendRequestsViewModel"/> is for an exercise — what the sweep of its schools is about
/// to touch.
/// </summary>
public sealed class SendExtensionRequestsViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required Guid ExtensionId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The exercise's name, as on its summary card: several releases may share a kind.</summary>
    public required string ExerciseLabel { get; init; }

    /// <summary>When the extension ended — a local wall-clock value.</summary>
    public required DateTime ExtensionEnd { get; init; }

    public required int SchoolCount { get; init; }

    public required int RequestsToSend { get; init; }
    public required int DraftsToCancel { get; init; }

    /// <summary>Submitted requests the Rules Engine has not decided yet. Sending leaves them (#536).</summary>
    public int RequestsWaiting { get; init; }

    /// <summary>Sending again is harmless, so the page says there is nothing to do rather than erroring.</summary>
    public bool HasNothingToDo => RequestsToSend == 0 && DraftsToCancel == 0 && RequestsWaiting == 0;

    /// <summary>Only waiting requests are left: nothing can be sent until the Rules Engine decides them.</summary>
    public bool OnlyWaiting => RequestsToSend == 0 && DraftsToCancel == 0 && RequestsWaiting > 0;

    // Invariant culture: "/" in a custom format is the culture's date separator.
    public string ExtensionEndDate => ExtensionEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string ExtensionEndTime => ExtensionEnd.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string Schools => SchoolCount == 1 ? "1 school" : $"{SchoolCount} schools";

    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{ExtensionId}/send-requests";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
