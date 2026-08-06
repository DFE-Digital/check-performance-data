using DfE.CheckPerformanceData.Application.Dashboard;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class AdminDashboardViewModel
{
    public sealed record WindowOption(Guid Id, string Title);

    public required IReadOnlyList<WindowOption> OpenWindows { get; init; }
    public Guid? SelectedWindowId { get; init; }
    public DashboardMetrics? Metrics { get; init; }
    public required int RefreshMinutes { get; init; }

    public bool HasOpenWindows => OpenWindows.Count > 0;

    /// <summary>Milliseconds until the current figures are due to refresh; floor of one minute
    /// so a stale cache entry never produces an instant reload loop.</summary>
    public double NextRefreshInMs => Metrics is null
        ? 0
        : Math.Max(
            TimeSpan.FromMinutes(1).TotalMilliseconds,
            (Metrics.RefreshedAtUtc.AddMinutes(RefreshMinutes) - DateTime.UtcNow).TotalMilliseconds);
}
