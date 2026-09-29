using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="IDeleteWindowService"/>
public sealed class DeleteWindowService(
    IWindowDeletionRepository repository,
    IWindowBlobStorage blobStorage) : IDeleteWindowService
{
    public async Task<DeleteWindowPreview> PreviewAsync(Guid windowId, CancellationToken cancellationToken)
    {
        var byStatus = await repository.CountRequestsByStatusAsync(windowId, cancellationToken);
        int Count(params RequestStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));

        return new DeleteWindowPreview
        {
            SubmittedNotSent = Count(RequestStatus.SubmittedUnCommitted),
            SentForProcessing = Count(RequestStatus.SubmittedCommitted),
            Drafts = Count(RequestStatus.InProgress, RequestStatus.ReadyToSubmit),
            WithdrawnOrCancelled = Count(RequestStatus.Withdrawn, RequestStatus.NotSubmitted),
            EgressRuns = await repository.CountEgressRunsAsync(windowId, cancellationToken)
        };
    }

    public async Task<bool> DeleteAsync(Guid windowId, CancellationToken cancellationToken)
    {
        // The rows go first. If the container delete then fails, what is left is a container that
        // nothing points at, and a new window never reuses the id. The other order can leave a
        // window that still shows on every page with none of its files.
        if (!await repository.DeleteAsync(windowId, cancellationToken))
            return false;

        await blobStorage.DeleteWindowContainerAsync(windowId, cancellationToken);
        return true;
    }
}
