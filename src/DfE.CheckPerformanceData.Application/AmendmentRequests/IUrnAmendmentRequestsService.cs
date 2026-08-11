namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface IUrnAmendmentRequestsService
{
    Task<UrnAmendmentRequestsResult> GetAllSubmittedAmendmentRequestsAsync(CancellationToken cancellationToken);
}
