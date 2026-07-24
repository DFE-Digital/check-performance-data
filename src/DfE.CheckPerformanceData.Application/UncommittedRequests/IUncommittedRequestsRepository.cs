using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UncommittedRequests;

public interface IUncommittedRequestsRepository
{
    // All change requests across every checking window, most recently submitted first.
    Task<IReadOnlyList<UncommittedRequestRow>> GetAllAsync(CancellationToken cancellationToken);

    // Replay projection of the SubmittedUnCommitted rows in currently-open windows,
    // used to rebuild RequestDocuments for the manual "send to Zendesk" admin action.
    Task<IReadOnlyList<ReplayRequestRow>> GetRequestsForOpenWindowsAsync(
        DateTime now, CancellationToken cancellationToken);

    // Sets a single ChangeRequest row's status by its Id.
    Task SetStatusAsync(Guid changeRequestId, RequestStatus status, CancellationToken cancellationToken);

    // Moves every InProgress / ReadyToSubmit draft in the currently-open windows to
    // NotSubmitted. Returns the number of rows changed.
    Task<int> MarkDraftsNotSubmittedForOpenWindowsAsync(DateTime now, CancellationToken cancellationToken);
}
