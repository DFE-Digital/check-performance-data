namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowService(IWindowRepository windowRepository, TimeProvider timeProvider): IWindowService
{
    public async Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetLocalNow();
        List<CheckingWindowDto> windows = await windowRepository.GetAllWindowsAsync(now.DateTime, cancellationToken);

        return new PageResult
        {
            Windows = windows
        };
    }

    public async Task<CheckingWindowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await windowRepository.GetByIdAsync(id, cancellationToken);

    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken) =>
        await windowRepository.UpdateAsync(window, cancellationToken);
    
    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken) =>
        await windowRepository.CreateAsync(window, cancellationToken);

}