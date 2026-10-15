namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface ICheckingDataReader
{
    /// <summary>The exercise's merged per-school file (with no release, the only file).</summary>
    Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken);

    /// <summary>One dataset's per-school file in the exercise's current release. Null with no release.</summary>
    Task<byte[]?> ReadDatasetAsync(CheckingDataExercise exercise, Guid datasetId, string laestab,
        CancellationToken cancellationToken);
    Task<byte[]?> ReadSchemaAsync(Guid windowId, string schemaFile, CancellationToken cancellationToken);

    /// <summary>
    /// The school has a file in what this exercise shows now: its merged file, or any dataset file
    /// of its current release. A cheap existence check; nothing is downloaded.
    /// </summary>
    Task<bool> HasSchoolDataAsync(Guid windowId, CheckingExerciseDto exercise, string laestab,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every school with a file in what this exercise shows now — its merged file, or any dataset
    /// file of its current release — as digits-only LAESTABs, from one listing (AB#307773 review,
    /// finding 7). Cached per release, because a release's files never change; the unversioned and
    /// legacy prefixes are listed each time. Empty for a missing container.
    /// </summary>
    Task<IReadOnlySet<string>> ListSchoolsWithDataAsync(Guid windowId, CheckingExerciseDto exercise,
        CancellationToken cancellationToken);
}
