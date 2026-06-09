using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    Task ConfirmRequestAsync(Guid windowId, RequestState journey);
    Task SaveDraftAsync(Guid windowId, RequestState journey, RequestStatus status);
    Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber);
    Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber);
}
