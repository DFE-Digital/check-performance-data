using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>The window's word for a learner, used by the two grids' column headings.</summary>
    public required LearnerNoun LearnerNoun { get; init; }

    /// <summary>
    /// One sentence per checking exercise the window runs (#320). The grid is deliberately left
    /// unsplit — both populations share one table and one bulk submit — but they do not share a
    /// deadline, so each is stated.
    /// </summary>
    public required IReadOnlyList<ExerciseDeadlineViewModel> Deadlines { get; init; }

    public required IReadOnlyList<AmendmentRequestRowViewModel> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestRowViewModel> SubmittedRows { get; init; }
}

/// <summary>One exercise's deadline sentence on the amendment requests page.</summary>
public sealed class ExerciseDeadlineViewModel
{
    public required CheckingExerciseType Exercise { get; init; }
    public required DateTime EndDate { get; init; }
    public required bool IsOpen { get; init; }

    /// <summary>The window's word for a learner: the pupil-data label names one.</summary>
    public required LearnerNoun LearnerNoun { get; init; }

    public string ExerciseLabel => ExerciseLabels.For(Exercise, LearnerNoun);

    // Checking-window dates are UK wall-clock values rather than UTC instants, so they are
    // formatted as they stand and never routed through LondonTime.
    public string DeadlineText =>
        $"{EndDate.ToString("htt").ToLowerInvariant()} on {EndDate:dddd d MMMM yyyy}";

    /// <summary>Past tense once the exercise has closed, matching the check-your-pupil-data page.</summary>
    public string Sentence => IsOpen
        ? $"Submit your {ExerciseLabel.ToLowerInvariant()} requests by {DeadlineText}"
        : $"The deadline for {ExerciseLabel.ToLowerInvariant()} requests passed at {DeadlineText}";
}

public sealed class SubmittedRequestRowViewModel
{
    public required string PupilName { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required string ReferenceNumber { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateTime Submitted { get; init; }

    public string TagClass => Status switch
    {
        RequestStatus.Withdrawn => "govuk-tag--grey",
        _ => "govuk-tag--green"
    };

    public string TagLabel => Status switch
    {
        RequestStatus.Withdrawn => "Withdrawn",
        _ => "Submitted"
    };

    public string SubmittedDateText => LondonTime.ToLondon(Submitted).ToString("d MMMM yyyy");

    public bool ShowDelete => Status != RequestStatus.Withdrawn;
}

public sealed class AmendmentRequestRowViewModel
{
    public required string PupilName { get; init; }
    public required RequestType RequestType { get; init; }
    public required string RequestTypeDescription { get; init; }
    public required RequestStatus Status { get; init; }
    public required string ReferenceNumber { get; init; }

    /// <summary>True when this row was in the bulk selection last taken into the bulk review, so the
    /// checkbox stays checked when the user comes back to the list.</summary>
    public bool IsSelected { get; init; }

    public string TagClass => Status switch
    {
        RequestStatus.ReadyToSubmit => "govuk-tag--blue",
        RequestStatus.InProgress => "govuk-tag--orange",
        _ => "govuk-tag--orange"
    };

    public string TagLabel => Status switch
    {
        RequestStatus.ReadyToSubmit => "Ready to submit",
        RequestStatus.InProgress => "In progress",
        _ => "In progress"
    };
}
