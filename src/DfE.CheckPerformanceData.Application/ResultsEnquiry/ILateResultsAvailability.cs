namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Whether a window's second late results file is still awaited. AB#296648.
///
/// Nearly all incorrect grades are corrected by that file, so while it is awaited the enquiry
/// journey tells the user to check it first. The service is never told separately — it derives the
/// answer from the results exercise's slots and its live release.
/// </summary>
public interface ILateResultsAvailability
{
    Task<bool> IsAwaitingSecondLateResultsAsync(Guid windowId, CancellationToken ct = default);
}
