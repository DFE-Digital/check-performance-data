using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageRepository
{
    /// <summary>
    /// Every window that has started by <paramref name="now"/>. Whether a started window is still
    /// shown is <c>ICheckingExerciseService.IsWindowShown</c>'s question, because an exercise's
    /// VisibleUntil can keep it shown after its end date.
    /// </summary>
    Task<List<CheckingWindowDto>> GetStartedWindowsAsync(DateTime now, string laestab, CancellationToken cancellationToken);
}