namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowStatusService() : IWindowStatusService
{
    public bool IsOpen(CheckingWindowDto window) => Brackets(window, DateTime.Now.ToUniversalTime());

    public IReadOnlyList<CheckingWindowDto> OpenWindows(IEnumerable<CheckingWindowDto> windows)
    {
        DateTime now = DateTime.Now.ToUniversalTime();
        return [.. windows.Where(w => Brackets(w, now))];
    }

    private static bool Brackets(CheckingWindowDto window, DateTime now) =>
        window.StartDate <= now && window.EndDate >= now;
}