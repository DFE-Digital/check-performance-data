namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Sends a submitted <see cref="RequestDocument"/> to the rules-engine queue.
/// Implemented in Infrastructure (Azure Storage Queue) so the Application layer
/// stays free of storage SDK types — same pattern as the blob clients.
/// </summary>
public interface IRequestQueueClient
{
    Task EnqueueRequestAsync(RequestDocument document);
}
