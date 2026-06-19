namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public interface IUncommittedRequestsRepository
{
    // SubmittedUnCommitted change requests whose window is open at <paramref name="now"/>,
    // most recently submitted first. Returns empty when no window is open.
    Task<IReadOnlyList<UncommittedRequestRow>> GetForOpenWindowsAsync(
        DateTime now, CancellationToken cancellationToken);
}
