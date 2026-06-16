namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface ISubmittedRequestService
{
    /// <summary>
    /// Rebuilds the read-only view of a submitted request from its persisted journey.
    /// Returns <c>null</c> when no journey is stored for the reference (e.g. it was never
    /// submitted) or its flow config cannot be resolved.
    /// </summary>
    Task<SubmittedRequestView?> GetAsync(Guid windowId, string referenceNumber);
}
