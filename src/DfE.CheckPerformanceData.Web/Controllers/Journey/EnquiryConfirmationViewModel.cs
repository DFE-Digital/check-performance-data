namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The "Results enquiry submitted" page (AB#296648). Deliberately minimal: after submission the
/// journey state is cleared, so the page has only the reference to show — everything the school
/// entered has gone, which is what makes "Report another issue" start clean.
/// </summary>
public sealed class EnquiryConfirmationViewModel
{
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }
}
