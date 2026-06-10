using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsViewModel
{
    public required Guid WindowId { get; init; }
    public required string DeadlineText { get; init; }
    public required IReadOnlyList<AmendmentRequestRowViewModel> Rows { get; init; }
}

public sealed class AmendmentRequestRowViewModel
{
    public required string PupilName { get; init; }
    public required string RequestType { get; init; }
    public required RequestStatus Status { get; init; }
    public required string ReferenceNumber { get; init; }

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
