namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Sends domain <see cref="AnalyticsEvent"/>s to the analytics sink. The Application
/// layer depends only on this contract; the Infrastructure adapter translates events
/// into the underlying analytics SDK. A no-op (<see cref="NullAnalyticsService"/>) is
/// registered when analytics is not configured, so callers never branch on whether it
/// is enabled.
/// </summary>
public interface IAnalyticsService
{
    Task TrackAsync(AnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default);
}
