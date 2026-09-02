namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class EstablishmentAmendmentRequestsViewModel
{
    public List<ActiveWindow> ActiveWindows { get; init; }
    public List<AmendmentItem> Rows { get; init; }
}

public sealed class ActiveWindow
{
    public string WindowId { get; init; }
    public string WindowTitle { get; init; }
    public string DeadlineText { get; init; }
}

public sealed class AmendmentItem
{

    public string PupilName { get; init; }
    public string ReferenceNumber { get; init; }
    public string RequestType { get; init; }
    public RequestStatus Status { get; init; }
    public string WindowName { get; init; }
    public string DateSubmitted { get; init; }
    public string WindowId { get; init; }
    public bool WindowIsOpen { get; init; }

    public string TagClass => Status switch
    {
        RequestStatus.ReadyToSubmit => "govuk-tag--blue",
        RequestStatus.SubmittedUnCommitted or RequestStatus.SubmittedCommitted => "govuk-tag--green",
        RequestStatus.Withdrawn or RequestStatus.NotSubmitted => "govuk-tag--grey",
        _ => "govuk-tag--orange"
    };

    public string TagLabel => Status switch
    {
        RequestStatus.InProgress => "In progress",
        RequestStatus.ReadyToSubmit => "Ready to submit",
        RequestStatus.SubmittedUnCommitted or RequestStatus.SubmittedCommitted => "Submitted",
        RequestStatus.Withdrawn => "Withdrawn",
        RequestStatus.NotSubmitted => "Not submitted",
        _ => Status.ToString()
    };
}