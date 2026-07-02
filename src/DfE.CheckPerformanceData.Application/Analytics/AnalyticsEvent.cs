namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A domain analytics event, expressed independently of any analytics SDK so the
/// Application layer never depends on the <c>DfeAnalytics.*</c> library — the
/// anti-corruption boundary. <see cref="IAnalyticsService"/> consumes these;
/// the Infrastructure adapter translates them into library events, routing
/// <see cref="AnalyticsField.Hidden"/> fields to the masked channel.
/// </summary>
public abstract record AnalyticsEvent
{
    /// <summary>Stable snake_case event name as it lands in the BigQuery events table.</summary>
    public abstract string EventType { get; }

    /// <summary>The event payload. Never include free text, identifiers, or other PII
    /// as plain fields — mark such data <see cref="AnalyticsField.Hidden"/>.</summary>
    public abstract IReadOnlyList<AnalyticsField> Fields { get; }
}
