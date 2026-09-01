namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowStatusService(TimeProvider timeProvider) : IWindowStatusService
{
    public bool IsOpen(CheckingWindowDto window) => Brackets(window, timeProvider.GetLocalNow().DateTime);

    public IReadOnlyList<CheckingWindowDto> OpenWindows(IEnumerable<CheckingWindowDto> windows)
    {
        DateTime now = timeProvider.GetLocalNow().DateTime;
        return [.. windows.Where(w => Brackets(w, now))];
    }

    private static bool Brackets(CheckingWindowDto window, DateTime now) =>
        window.StartDate <= now && window.EndDate >= now;
}