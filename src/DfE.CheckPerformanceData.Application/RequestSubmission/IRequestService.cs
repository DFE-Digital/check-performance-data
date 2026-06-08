using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    Task ConfirmRequestAsync(Guid windowId, RequestState journey);
    Task SaveDraftAsync(Guid windowId, RequestState journey);
    Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber);
}
