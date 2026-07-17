using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required string DeadlineText { get; init; }
    public required IReadOnlyList<AmendmentRequestRowViewModel> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestRowViewModel> SubmittedRows { get; init; }
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
