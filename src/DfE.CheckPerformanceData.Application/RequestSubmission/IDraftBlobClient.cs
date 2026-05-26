using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IDraftBlobClient
{
    Task SaveDraftAsync(Guid windowId, string referenceNumber, RequestState state);
    Task<RequestState?> GetDraftAsync(Guid windowId, string referenceNumber);
}
