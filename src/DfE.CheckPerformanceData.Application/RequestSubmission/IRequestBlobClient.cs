namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestBlobClient
{
    Task SaveRequestAsync(Guid windowId, string cypmdId, RequestDocument document);
}
