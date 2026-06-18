namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface ISubmittedRequestService
{
    /// <summary>
    /// Rebuilds the read-only view of a submitted request from its persisted journey.
    /// Returns <c>null</c> when no journey is stored for the reference (e.g. it was never
    /// submitted) or its flow config cannot be resolved.
    /// </summary>
    Task<SubmittedRequestView?> GetAsync(Guid windowId, string referenceNumber);

    /// <summary>
    /// Read-only view of a "confirm pupil data is correct" submission, read from the
    /// <c>ChangeRequests</c> row (these have no journey blob). Returns <c>null</c> when no
    /// row matches for the current user's school, or the row is not a ConfirmCorrect request.
    /// </summary>
    Task<ConfirmDataCorrectView?> GetConfirmDataCorrectAsync(Guid windowId, string referenceNumber);
}
