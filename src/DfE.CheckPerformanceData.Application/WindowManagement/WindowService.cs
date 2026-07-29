namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowService(IWindowRepository windowRepository, TimeProvider timeProvider): IWindowService
{
    private const int WindowClosingHour = 17;

    public async Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetLocalNow();
        List<CheckingWindowDto> windows = await windowRepository.GetAllWindowsAsync(cancellationToken);

        foreach (CheckingWindowDto window in windows)
        {
            window.IsOpen = window.StartDate <= now.DateTime && now.DateTime <= window.EndDate;
        }

        return new PageResult
        {
            Windows = windows
        };
    }

    public async Task<CheckingWindowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await windowRepository.GetByIdAsync(id, cancellationToken);

    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        ApplyWindowOpeningHours(window);
        EnsureDatasetsMatchType(window);
        await windowRepository.UpdateAsync(window, cancellationToken);
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        ApplyWindowOpeningHours(window);
        EnsureDatasetsMatchType(window);
        return await windowRepository.CreateAsync(window, cancellationToken);
    }

    /// <summary>
    /// A window's dataset set is decided by its type, so changing the type (e.g. KS4June -> Post16)
    /// adds or removes dataset slots. Files already uploaded to a slot that survives are kept.
    /// </summary>
    private static void EnsureDatasetsMatchType(CheckingWindowDto window)
    {
        List<CheckingWindowDatasetDto> wanted = [];

        foreach (CheckingWindowDatasetDto expected in WindowDatasets.DefaultsFor(window.CheckingWindowType))
        {
            CheckingWindowDatasetDto? existing = window.Datasets.SingleOrDefault(d => d.Name == expected.Name);
            wanted.Add(existing ?? expected);
        }

        window.Datasets = wanted;
    }

    /// <summary>
    /// A checking window always opens at midnight on its start date and closes at 17:00 on its end
    /// date. Admins choose dates only, so any time component carried on the supplied dates is replaced.
    /// </summary>
    private static void ApplyWindowOpeningHours(CheckingWindowDto window)
    {
        window.StartDate = window.StartDate.Date;
        window.EndDate = window.EndDate.Date.AddHours(WindowClosingHour);
    }
}
