namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// The "pull decisions from Zendesk" confirmation page (#746): how many of the exercise's
/// submitted requests the pull will check, before anything is read.
/// </summary>
public sealed class PullDecisionsViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The exercise's name, as on its summary card: several releases may share a kind.</summary>
    public required string ExerciseLabel { get; init; }

    public required int SubmittedTotal { get; init; }

    /// <summary>Submitted requests that carry a Zendesk ticket id; the rest count as "no ticket".</summary>
    public required int WithTicket { get; init; }

    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/pull-decisions";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
