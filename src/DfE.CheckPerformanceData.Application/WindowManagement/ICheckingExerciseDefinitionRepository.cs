namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>One exercise and the window it belongs to, addressed by the exercise's own id.</summary>
public sealed record CheckingExerciseDefinition(Guid WindowId, CheckingExerciseDto Exercise);

/// <summary>
/// Reads and stamps one exercise. Separate from <see cref="IWindowRepository"/> because an ingress
/// run knows only the exercise it was asked to run, never which window holds it.
/// </summary>
public interface ICheckingExerciseDefinitionRepository
{
    Task<CheckingExerciseDefinition?> GetAsync(Guid exerciseId, CancellationToken cancellationToken);

    Task StampAsync(Guid exerciseId, DateTime validatedAt, string ingressChecksum,
        string schemaChecksum, CancellationToken cancellationToken);

    /// <summary>
    /// Records a finished run as the exercise's next release, makes it the current release and
    /// stamps the exercise validated, in one save. The release's <c>Number</c> is given here, not
    /// by the caller. Returns the release as stored.
    /// </summary>
    Task<CheckingExerciseReleaseDto> PublishReleaseAsync(Guid exerciseId, CheckingExerciseReleaseDto release,
        string ingressChecksum, string schemaChecksum, CancellationToken cancellationToken);

    /// <summary>
    /// Makes an existing release of the exercise the current release. False when the exercise has
    /// no release with that id.
    /// </summary>
    Task<bool> SetCurrentReleaseAsync(Guid exerciseId, Guid releaseId, CancellationToken cancellationToken);
}
