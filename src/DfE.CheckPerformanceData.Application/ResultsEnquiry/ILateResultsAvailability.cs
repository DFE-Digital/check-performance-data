namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Whether a school's second late results file has landed for a window. AB#296648.
///
/// Nearly all incorrect grades are corrected by that file, so the enquiry journey tells the user to
/// check it first. The service is never told separately whether it exists — it derives the answer
/// from the results data it already holds.
/// </summary>
public interface ILateResultsAvailability
{
    Task<bool> IsSecondLateResultsAvailableAsync(Guid windowId, string laestab, CancellationToken ct = default);
}
