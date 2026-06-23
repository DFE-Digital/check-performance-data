using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Persists the full <see cref="RequestState"/> journey to the per-window "requests"
/// blob (<c>requests/{referenceNumber}.json</c>). Used both for in-progress drafts
/// ("Save and continue later") and to retain a submitted request's journey so the
/// read-only view can rebuild it without depending on the queued <c>RequestDocument</c>.
/// </summary>
public interface IRequestStateBlobClient
{
    Task SaveAsync(Guid windowId, string referenceNumber, RequestState state);
    Task<RequestState?> GetAsync(Guid windowId, string referenceNumber);

    /// <summary>
    /// Removes the persisted journey blob. Used when a draft request is hard-deleted.
    /// No-op if the blob does not exist.
    /// </summary>
    Task DeleteAsync(Guid windowId, string referenceNumber);
}
