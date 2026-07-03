namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Default <see cref="IAnalyticsService"/> that discards events. Registered when no
/// analytics sink is configured (local dev, review apps, environments without GCP),
/// so the worker and web app boot and run unchanged. The real Infrastructure adapter
/// replaces it when analytics is enabled.
/// </summary>
public sealed class NullAnalyticsService : IAnalyticsService
{
    public Task TrackAsync(AnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
