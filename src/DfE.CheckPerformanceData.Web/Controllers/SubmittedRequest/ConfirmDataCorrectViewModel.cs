using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class ConfirmDataCorrectViewModel
{
    public required Guid WindowId { get; init; }
    public required RequestStatus Status { get; init; }
    public bool ConfirmingDelete { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public string? WithdrawnByEmail { get; init; }
    public string WithdrawnAtText { get; init; } = string.Empty;
    public required string ReferenceNumber { get; init; }

    /// <summary>
    /// The window's word for a learner. A confirm-data-correct request has no journey state to
    /// carry it, so the controller reads it from the window itself.
    /// </summary>
    public required LearnerNoun LearnerNoun { get; init; }

    public string RequestTypeDisplay => $"Confirm {LearnerNoun.Singular} data is correct";

    public string SubmittedAtText => LondonTime.ToSubmittedAtText(SubmittedAt);

    public string ByLineTitle => Status switch
    {
        RequestStatus.Withdrawn => "Withdrawn by",
        RequestStatus.InProgress or RequestStatus.ReadyToSubmit => "Last saved by",
        _ => "Submitted by"
    };

    public bool ShowDeleteButton => Status != RequestStatus.Withdrawn;

    public string ConfirmDeleteTitle =>
        $"Are you sure you want to delete the confirmation that {LearnerNoun.Singular} data is correct?";

    public bool ShowReferenceNumber => !ConfirmingDelete && Status is not (RequestStatus.InProgress or RequestStatus.ReadyToSubmit);
}
