namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICsvSchemaFileProcessor
{
    /// <summary>
    /// Validates the ingress CSV against the schema, writing one JSON file per school to storage.
    /// Streams a <see cref="ValidationProgress"/> per stage so a caller can render live progress.
    /// Every school group is validated up front and all errors are collected; data files are only
    /// written when the whole file is valid, so a run either commits all clean data or writes
    /// nothing.
    /// </summary>
    /// <param name="validateOnly">
    /// When true, the run validates and reports every error but writes no data files, for callers
    /// that only want to check a file.
    /// </param>
    /// <param name="clearExistingFiles">
    /// When true, output left by a previous run (the per-school data files and the error log) is
    /// removed before processing starts. Ignored on a validate-only run.
    /// </param>
    IAsyncEnumerable<ValidationProgress> ProcessAsync(
        Guid checkingWindowId,
        string inputCsvFile,
        string inputCsvChecksum,
        string schemaFile,
        string schemaChecksum,
        bool validateOnly = false,
        bool clearExistingFiles = false,
        CancellationToken cancellationToken = default);
}
