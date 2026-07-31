namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICsvSchemaFileProcessor
{
    /// <summary>
    /// Validates every dataset's ingress CSV against that dataset's own schema, then writes one
    /// merged JSON file per school to storage. Streams a <see cref="ValidationProgress"/> per
    /// stage so a caller can render live progress.
    ///
    /// All datasets are validated up front and all errors are collected; data files are only
    /// written when EVERY dataset is valid, so a run either commits all clean data or writes
    /// nothing. Records from all datasets for the same LAESTAB are merged into that school's
    /// single <c>data/{laestab}_pupils.json</c> — a Post16 window's included and non-included
    /// populations therefore land in one file, which is why the merge must happen within a
    /// single run (a second run's write would overwrite the first's).
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
        IReadOnlyList<IngressDataset> datasets,
        bool validateOnly = false,
        bool clearExistingFiles = false,
        CancellationToken cancellationToken = default);
}
