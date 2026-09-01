namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowStatusService
{
    bool IsOpen(CheckingWindowDto window);
    IReadOnlyList<CheckingWindowDto> OpenWindows(IEnumerable<CheckingWindowDto> windows);
}