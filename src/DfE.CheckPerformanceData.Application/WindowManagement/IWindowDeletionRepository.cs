using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>The database half of <see cref="IDeleteWindowService"/>.</summary>
public interface IWindowDeletionRepository
{
    /// <summary>Every change request on the window, counted by status. A status with no rows is absent.</summary>
    Task<IReadOnlyDictionary<RequestStatus, int>> CountRequestsByStatusAsync(
        Guid windowId, CancellationToken cancellationToken);

    /// <summary>
    /// The submitted requests on the window that are already on their way to Zendesk: queued for
    /// a ticket or holding one (#536). They are counted under Submitted by
    /// <see cref="CountRequestsByStatusAsync"/> too.
    /// </summary>
    Task<int> CountSentForProcessingAsync(Guid windowId, CancellationToken cancellationToken);

    Task<int> CountEgressRunsAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the window and every row that belongs to it, in one transaction. False when no
    /// window has that id.
    /// </summary>
    Task<bool> DeleteAsync(Guid windowId, CancellationToken cancellationToken);
}

/// <summary>The blob half of <see cref="IDeleteWindowService"/>.</summary>
public interface IWindowBlobStorage
{
    /// <summary>
    /// Deletes the window's <c>{windowId}</c> container: its data, schemas, request state,
    /// submitted request documents and evidence uploads. No error when there is no container.
    /// </summary>
    Task DeleteWindowContainerAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every blob in the window's container under one exercise's own prefixes
    /// (<see cref="CheckingExerciseBlobPaths.OwnedPrefixes"/>): its uploads, outputs, releases and
    /// logs. Only for an exercise on the exercise-id storage; an older exercise shares its
    /// prefixes with nothing that says which blobs are its own.
    /// </summary>
    Task DeleteExerciseBlobsAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken);
}
