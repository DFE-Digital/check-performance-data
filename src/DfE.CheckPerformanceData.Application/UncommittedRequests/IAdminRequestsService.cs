namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public interface IAdminRequestsService
{
    // All change requests across every checking window, with their rules-engine outcome.
    Task<IReadOnlyList<UncommittedRequestRow>> GetAsync(CancellationToken cancellationToken);

    // Quick-and-dirty test hook: rebuild a RequestDocument for every SubmittedUnCommitted
    // request in the current open window(s) and drop each onto the Zendesk queue. Returns
    // the number of documents enqueued.
    Task<int> ProcessCloseWindowEvent(CancellationToken cancellationToken);
}
