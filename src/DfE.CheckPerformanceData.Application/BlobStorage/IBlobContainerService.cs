namespace DfE.CheckPerformanceData.Application.BlobStorage;

public interface IBlobContainerService
{
    Task EnsureContainerExistsAsync(Guid windowId);
}
