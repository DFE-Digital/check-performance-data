using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The second late results file is awaited while the results exercise has a slot for it in use (not
/// retired) and the live release has not read that slot. October: awaited. November, once the file
/// is run: not. February, when the slot is retired with the other replaced files: not, so the
/// guidance stops.
///
/// This is the ONLY place in the service that decides this. Anything that needs the answer asks
/// here, so the interstitial and every future consumer cannot drift apart.
/// </summary>
/// <remarks>
/// The answer is per file, not per school. It used to be "does this school hold any LR2 row", which
/// told a school with no late results to wait for a file that had already arrived.
/// </remarks>
public sealed class LateResultsAvailability(ICheckingExerciseStorageResolver exercises) : ILateResultsAvailability
{
    public async Task<bool> IsAwaitingSecondLateResultsAsync(Guid windowId, CancellationToken ct = default)
    {
        var exercise = await exercises.ResolveAsync(windowId, CheckingExerciseType.ResultsEnquiry, ct);
        if (exercise is null) return false;

        var published = exercise.PublishedDatasets.Select(d => d.Id).ToHashSet();
        return exercise.Datasets.Any(d =>
            !d.Retired && ResultsSources.IsSecondLateResults(d.SourceFile) && !published.Contains(d.Id));
    }
}
