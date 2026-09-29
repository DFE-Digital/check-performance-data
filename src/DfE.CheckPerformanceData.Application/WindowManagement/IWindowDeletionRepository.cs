using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>The database half of <see cref="IDeleteWindowService"/>.</summary>
public interface IWindowDeletionRepository
{
    /// <summary>Every change request on the window, counted by status. A status with no rows is absent.</summary>
    Task<IReadOnlyDictionary<RequestStatus, int>> CountRequestsByStatusAsync(
        Guid windowId, CancellationToken cancellationToken);

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
}
