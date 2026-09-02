using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class EstablishmentAmendmentRequestsViewModelTests
{
    [Theory]
    [InlineData(RequestStatus.InProgress, "In progress", "govuk-tag--orange")]
    [InlineData(RequestStatus.ReadyToSubmit, "Ready to submit", "govuk-tag--blue")]
    [InlineData(RequestStatus.SubmittedUnCommitted, "Submitted", "govuk-tag--green")]
    [InlineData(RequestStatus.SubmittedCommitted, "Submitted", "govuk-tag--green")]
    [InlineData(RequestStatus.Withdrawn, "Withdrawn", "govuk-tag--grey")]
    [InlineData(RequestStatus.NotSubmitted, "Not submitted", "govuk-tag--grey")]
    public void AmendmentItem_presents_a_friendly_status(
        RequestStatus status,
        string expectedLabel,
        string expectedClass)
    {
        AmendmentItem item = new() { Status = status };

        Assert.Equal(expectedLabel, item.TagLabel);
        Assert.Equal(expectedClass, item.TagClass);
    }
}
