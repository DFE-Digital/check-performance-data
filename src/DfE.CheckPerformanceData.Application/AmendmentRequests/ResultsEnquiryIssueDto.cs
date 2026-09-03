namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>
/// One row of the Issues tab (AB#298325): a submitted results enquiry with the two display fields
/// recovered from its journey blob. CypmdId and QualificationText are empty strings when the blob
/// is missing or unreadable — the tab shows the row regardless, because the ChangeRequests row is
/// the record of truth.
/// </summary>
public sealed class ResultsEnquiryIssueDto
{
    public required string PupilName { get; init; }
    public required DateTime Submitted { get; init; }
    public required string CypmdId { get; init; }

    /// <summary>The enquiry kind as user-facing text, e.g. "Missing qualification" — the suffix of
    /// RequestTypeDescription ("Results enquiry - Missing qualification"), which
    /// SubmitResultsEnquiryAsync writes from a closed switch over the three kinds.</summary>
    public required string TypeLabel { get; init; }

    public required string QualificationText { get; init; }
    public required string ReferenceNumber { get; init; }
}
