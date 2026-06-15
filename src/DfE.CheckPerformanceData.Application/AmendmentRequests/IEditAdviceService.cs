using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface IEditAdviceService
{
    /// <summary>
    /// Builds the advice screen model for a resumed draft. Returns <c>null</c> when the
    /// journey is incomplete, the persisted request cannot be found, or no flow config exists.
    /// </summary>
    Task<AmendmentAdvice?> BuildAsync(Guid windowId, string referenceNumber, RequestState journey);
}
