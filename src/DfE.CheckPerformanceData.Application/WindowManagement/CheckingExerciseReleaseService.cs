namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>What happened when an admin asked to make a release live.</summary>
public enum MakeReleaseLiveResult
{
    /// <summary>The release is now the one schools see.</summary>
    MadeLive,

    /// <summary>The release was already live. Nothing changed.</summary>
    AlreadyLive,

    /// <summary>The window, the exercise or the release does not exist, or they do not belong together.</summary>
    NotFound
}

/// <summary>Chooses which release of a checking exercise schools see.</summary>
public interface ICheckingExerciseReleaseService
{
    /// <summary>
    /// Makes an earlier (or later) release of the exercise live again. The release's output is
    /// already in storage, so nothing is re-run and nothing is re-uploaded.
    /// </summary>
    Task<MakeReleaseLiveResult> MakeLiveAsync(Guid windowId, Guid exerciseId, Guid releaseId,
        CancellationToken cancellationToken);
}

public sealed class CheckingExerciseReleaseService(ICheckingExerciseDefinitionRepository definitions)
    : ICheckingExerciseReleaseService
{
    public async Task<MakeReleaseLiveResult> MakeLiveAsync(Guid windowId, Guid exerciseId, Guid releaseId,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(exerciseId, cancellationToken);

        // The window id comes from the route. An exercise of another window must not be changed
        // from this window's page.
        if (definition is null || definition.WindowId != windowId
            || definition.Exercise.Releases.All(r => r.Id != releaseId))
            return MakeReleaseLiveResult.NotFound;

        if (definition.Exercise.CurrentReleaseId == releaseId)
            return MakeReleaseLiveResult.AlreadyLive;

        return await definitions.SetCurrentReleaseAsync(exerciseId, releaseId, cancellationToken)
            ? MakeReleaseLiveResult.MadeLive
            : MakeReleaseLiveResult.NotFound;
    }
}
