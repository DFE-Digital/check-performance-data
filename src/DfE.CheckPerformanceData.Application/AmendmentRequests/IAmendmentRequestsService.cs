namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface IAmendmentRequestsService
{
    /// <param name="issueSearch">Optional Issues-tab filter: case-insensitive substring match on
    /// the pupil's first or last name (AB#298325). Null/whitespace means unfiltered.</param>
    Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId, string? issueSearch = null);
}
