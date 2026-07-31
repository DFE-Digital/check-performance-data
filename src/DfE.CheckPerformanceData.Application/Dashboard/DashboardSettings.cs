namespace DfE.CheckPerformanceData.Application.Dashboard;

/// <summary>Bound from the "Dashboard" configuration section.</summary>
public sealed class DashboardSettings
{
    /// <summary>How long computed metrics are served from cache before re-aggregating.</summary>
    public int RefreshMinutes { get; set; } = 15;

    /// <summary>
    /// The refresh interval actually used, never below one minute. IMemoryCache rejects a
    /// non-positive relative expiration, so a configured 0 or negative would otherwise turn
    /// every dashboard request into a 500 rather than simply disabling the cache.
    /// </summary>
    public int EffectiveRefreshMinutes => Math.Max(1, RefreshMinutes);
}
