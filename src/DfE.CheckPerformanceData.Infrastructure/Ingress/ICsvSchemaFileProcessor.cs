using DfE.CheckPerformanceData.Domain.Enums;

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
    /// single data file — a Post16 window's included and non-included populations therefore land
    /// in one file, which is why the merge must happen within a single run (a second run's write
    /// would overwrite the first's).
    ///
    /// A run belongs to one checking exercise, not to the window (#316). The exercise selects the
    /// blob prefix everything is written under and, crucially, scopes the clear sweep — two
    /// exercises share one <c>{windowId}</c> container, so an unscoped sweep would let one
    /// exercise's run destroy another's output.
    /// </summary>
    /// <param name="exercise">
    /// The checking exercise this run belongs to. Selects the prefix via
    /// <c>CheckingExerciseBlobPaths</c> and scopes the sweep. The rule that a window type's ingress
    /// files are ingested in a single run still holds, but now within an exercise.
    /// </param>
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
        CheckingExerciseType exercise,
        IReadOnlyList<IngressDataset> datasets,
        bool validateOnly = false,
        bool clearExistingFiles = false,
        CancellationToken cancellationToken = default);
}
