namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Derives second-late-results availability from the presence of any result tagged
/// <see cref="ResultsFileTags.Post16LateResults2"/> in the school's results file.
///
/// This is the ONLY place in the service that decides what "the second late results file is
/// available" means. Anything that needs the answer asks here, so the interstitial and every future
/// consumer cannot drift apart.
///
/// PARKED AB#296648: the October-gating decision (whether "Incorrect grade" is even selectable
/// before the second late results file exists) may add a second consumer of this. That is why it is
/// a seam rather than an inline check in the controller.
/// </summary>
public sealed class LateResultsAvailability(IStudentResultsClient results) : ILateResultsAvailability
{
    public Task<bool> IsSecondLateResultsAvailableAsync(
        Guid windowId, string laestab, CancellationToken ct = default)
        => results.AnyForSourceAsync(windowId, laestab, ResultsFileTags.Post16LateResults2, ct);
}
