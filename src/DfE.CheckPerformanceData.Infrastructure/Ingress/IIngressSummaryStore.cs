namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface IIngressSummaryStore
{
    Task<bool> ArchiveCurrentAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken);
    Task<bool> WriteAsync(Guid windowId, Guid exerciseId, IngressSummaryDocument summary, CancellationToken cancellationToken);
}
