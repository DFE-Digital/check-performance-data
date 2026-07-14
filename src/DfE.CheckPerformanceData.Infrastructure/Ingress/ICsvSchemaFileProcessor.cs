namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICsvSchemaFileProcessor
{
    /// <summary>
    /// Validates the ingress CSV against the schema, writing one JSON file per school to storage.
    /// Streams a <see cref="ValidationProgress"/> per stage so a caller can render live progress.
    /// The run stops at the first invalid school group and removes any data files it wrote, so a
    /// run either commits all clean data or leaves storage untouched.
    /// </summary>
    IAsyncEnumerable<ValidationProgress> ProcessAsync(
        Guid checkingWindowId,
        string inputCsvFile,
        string inputCsvChecksum,
        string schemaFile,
        string schemaChecksum,
        CancellationToken cancellationToken = default);
}
