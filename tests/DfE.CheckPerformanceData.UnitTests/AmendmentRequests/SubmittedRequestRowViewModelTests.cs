using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// The Requests tab's status tag after #746: a stored Zendesk decision refines the tag of a
// submitted request; a withdrawn request never shows a decision (spec FR-012).
public sealed class SubmittedRequestRowViewModelTests
{
    private static SubmittedRequestRowViewModel Row(
        RequestStatus status, ZendeskDecision? decision, string? decisionReason = null) => new()
    {
        PupilName = "Jane Smith",
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove - Permanently left England",
        ReferenceNumber = "REF010",
        Status = status,
        Submitted = new DateTime(2026, 6, 16, 9, 30, 0),
        Decision = decision,
        DecisionReason = decisionReason
    };

    [Fact]
    public void TheRowCarriesTheStoredDecisionReason()
    {
        var row = Row(RequestStatus.Submitted, ZendeskDecision.Accepted, "Inclusion criteria met");
        Assert.Equal("Inclusion criteria met", row.DecisionReason);
    }

    [Fact]
    public void ARowWithoutAStoredDecisionReasonStaysNull()
    {
        var row = Row(RequestStatus.Submitted, ZendeskDecision.Rejected);
        Assert.Null(row.DecisionReason);
    }

    [Fact]
    public void SubmittedWithoutADecision_ShowsSubmittedGreen()
    {
        var row = Row(RequestStatus.Submitted, null);
        Assert.Equal("Submitted", row.TagLabel);
        Assert.Equal("govuk-tag--green", row.TagClass);
    }

    [Fact]
    public void SubmittedWithAccepted_ShowsAcceptedGreen()
    {
        var row = Row(RequestStatus.Submitted, ZendeskDecision.Accepted);
        Assert.Equal("Accepted", row.TagLabel);
        Assert.Equal("govuk-tag--green", row.TagClass);
    }

    [Fact]
    public void SubmittedWithRejected_ShowsRejectedRed()
    {
        var row = Row(RequestStatus.Submitted, ZendeskDecision.Rejected);
        Assert.Equal("Rejected", row.TagLabel);
        Assert.Equal("govuk-tag--red", row.TagClass);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ZendeskDecision.Accepted)]
    [InlineData(ZendeskDecision.Rejected)]
    public void Withdrawn_NeverShowsTheDecision(ZendeskDecision? decision)
    {
        var row = Row(RequestStatus.Withdrawn, decision);
        Assert.Equal("Withdrawn", row.TagLabel);
        Assert.Equal("govuk-tag--grey", row.TagClass);
    }
}
