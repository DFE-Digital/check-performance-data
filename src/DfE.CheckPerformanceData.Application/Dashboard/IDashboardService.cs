using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.Dashboard;

public interface IDashboardService
{
    /// <summary>
    /// Engagement + amendment metrics for one checking window. Served from a per-window
    /// cache for DashboardSettings.RefreshMinutes; RefreshedAtUtc tells the caller when the
    /// figures were actually computed.
    /// </summary>
    Task<DashboardMetrics> GetMetricsAsync(CheckingWindowDto window, CancellationToken cancellationToken = default);
}
