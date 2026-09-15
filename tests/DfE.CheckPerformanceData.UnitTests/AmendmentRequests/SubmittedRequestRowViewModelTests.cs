using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// Closing an exercise flips its submitted rows from SubmittedUnCommitted to SubmittedCommitted.
// The Requests tab must keep showing them as "Submitted" — the school did submit them — but a
// committed request is already on the Zendesk queue, so withdrawing it would change nothing
// downstream. The Delete link therefore exists only while the row is still uncommitted.
public sealed class SubmittedRequestRowViewModelTests
{
    [Theory]
    [InlineData(RequestStatus.SubmittedUnCommitted)]
    [InlineData(RequestStatus.SubmittedCommitted)]
    public void TagLabel_ReadsSubmitted_WhetherOrNotCommitted(RequestStatus status)
    {
        var row = Row(status);

        Assert.Equal("Submitted", row.TagLabel);
        Assert.Equal("govuk-tag--green", row.TagClass);
    }

    [Fact]
    public void ShowDelete_IsTrue_OnlyWhileSubmittedUnCommitted()
    {
        Assert.True(Row(RequestStatus.SubmittedUnCommitted).ShowDelete);
    }

    [Theory]
    [InlineData(RequestStatus.SubmittedCommitted)]
    [InlineData(RequestStatus.Withdrawn)]
    public void ShowDelete_IsFalse_OnceCommittedOrWithdrawn(RequestStatus status)
    {
        Assert.False(Row(status).ShowDelete);
    }

    private static SubmittedRequestRowViewModel Row(RequestStatus status) => new()
    {
        PupilName = "Alice Smith",
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove",
        ReferenceNumber = "CYPMD_KS4June_ABC1234",
        Status = status,
        Submitted = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)
    };
}
