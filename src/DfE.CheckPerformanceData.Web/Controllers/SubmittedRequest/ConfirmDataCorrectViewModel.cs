using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class ConfirmDataCorrectViewModel
{
    public required Guid WindowId { get; init; }
    public required RequestStatus Status { get; init; }
    public bool ConfirmingDelete { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public required string ReferenceNumber { get; init; }

    public string RequestTypeDisplay => "Confirm pupil data is correct";

    public string SubmittedAtText => LondonTime.ToSubmittedAtText(SubmittedAt);

    public bool ShowDeleteButton => Status != RequestStatus.Withdrawn;

    public string ConfirmDeleteTitle => "Are you sure you want to delete the confirmation that pupil data is correct?";
}
