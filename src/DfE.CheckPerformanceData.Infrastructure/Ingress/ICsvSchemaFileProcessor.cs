namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICsvSchemaFileProcessor
{
    Task<ProcessingResult> ProcessAsync(
        Guid checkingWindowId,
        string inputCsvFile,
        string schemaFile,
        CancellationToken cancellationToken = default);
}