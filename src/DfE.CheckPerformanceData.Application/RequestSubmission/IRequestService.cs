using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    Task ConfirmRequestAsync(Guid windowId, RequestState journey);
}
