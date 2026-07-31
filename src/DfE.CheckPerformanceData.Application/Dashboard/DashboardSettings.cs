namespace DfE.CheckPerformanceData.Application.Dashboard;

/// <summary>Bound from the "Dashboard" configuration section.</summary>
public sealed class DashboardSettings
{
    /// <summary>How long computed metrics are served from cache before re-aggregating.</summary>
    public int RefreshMinutes { get; set; } = 15;
}
