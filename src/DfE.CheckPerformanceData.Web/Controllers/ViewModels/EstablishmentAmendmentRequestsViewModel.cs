using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class EstablishmentAmendmentRequestsViewModel
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

public sealed class AmendmentItem {

    public string PupilName { get; init; }
    public string ReferenceNumber { get; init; }
    public string RequestType { get; init; }
    public RequestStatus Status { get; init; }
    public string WindowName { get; init; }
    public string DateSubmitted { get; init; }
    public string WindowId { get; init; }
    public bool WindowIsOpen { get; init; }
}