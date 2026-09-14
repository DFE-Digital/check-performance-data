namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface IAmendmentRequestsService
{
    Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId);
}
