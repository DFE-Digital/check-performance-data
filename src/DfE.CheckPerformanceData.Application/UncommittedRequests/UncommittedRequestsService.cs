namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public sealed class UncommittedRequestsService(
    IUncommittedRequestsRepository repository,
    TimeProvider timeProvider) : IUncommittedRequestsService
{
    public Task<IReadOnlyList<UncommittedRequestRow>> GetAsync(CancellationToken cancellationToken) =>
        // Checking-window dates are stored as local timestamps; mirror LandingPageRepository.
        repository.GetForOpenWindowsAsync(timeProvider.GetLocalNow().DateTime, cancellationToken);
}
