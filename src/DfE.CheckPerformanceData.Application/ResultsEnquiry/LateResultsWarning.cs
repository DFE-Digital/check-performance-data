using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads the admin's "Show late results warning" choice from the window's live results enquiry.
///
/// This is the ONLY place in the service that decides this. Anything that needs the answer asks
/// here, so the interstitial and every future consumer cannot drift apart.
/// </summary>
/// <remarks>
/// It used to be derived from the data: "the second late results slot is in use and the live release
/// has not read it". That tied the guidance to one file of one supplier feed, so a results enquiry
/// whose late results arrive another way could not use it. The choice names no file.
/// </remarks>
public sealed class LateResultsWarning(ICheckingExerciseStorageResolver exercises) : ILateResultsWarning
{
    public async Task<bool> ShowAsync(Guid windowId, CancellationToken ct = default)
    {
        var exercise = await exercises.ResolveAsync(windowId, CheckingExerciseType.ResultsEnquiry, ct);
        return exercise?.ShowLateResultsWarning == true;
    }
}
