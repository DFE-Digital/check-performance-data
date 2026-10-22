namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Whether a window's results enquiry shows its late results guidance before an incorrect-grade
/// enquiry. AB#296648.
///
/// Late results correct many incorrect grades, so while they are still to come the enquiry journey
/// tells the user to check them first. The admin decides this on the results enquiry exercise
/// (<see cref="WindowManagement.CheckingExerciseDto.ShowLateResultsWarning"/>).
/// </summary>
public interface ILateResultsWarning
{
    Task<bool> ShowAsync(Guid windowId, CancellationToken ct = default);
}
