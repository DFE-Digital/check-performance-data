namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public interface IUncommittedRequestsService
{
    // SubmittedUnCommitted change requests for the current open checking window,
    // with their rules-engine outcome.
    Task<IReadOnlyList<UncommittedRequestRow>> GetAsync(CancellationToken cancellationToken);
}
