namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Helpers for emitting analytics from request-handling code paths.
/// </summary>
public static class AnalyticsServiceExtensions
{
    /// <summary>
    /// Tracks an event best-effort: any failure is swallowed so analytics can never
    /// break the caller (e.g. a user's submission). Use this from controllers and
    /// other user-facing paths. The underlying sender logs its own send failures.
    /// </summary>
    public static async Task TrackSafeAsync(this IAnalyticsService analytics, AnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analytics);
        try
        {
            await analytics.TrackAsync(analyticsEvent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort telemetry: never propagate to the caller.
        }
    }
}
