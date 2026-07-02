using System.Globalization;
using Dfe.Analytics.Events;
using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Infrastructure.Analytics;

/// <summary>
/// <see cref="IAnalyticsService"/> adapter over the dfe-analytics
/// <see cref="IEventSender"/>. Translates a domain <see cref="AnalyticsEvent"/> into a
/// library <see cref="Event"/>: each <see cref="AnalyticsField"/> becomes plain
/// <c>DATA</c> or, when <see cref="AnalyticsField.Hidden"/>, masked <c>hidden_DATA</c>.
/// Values are stringified because the library's event payload is string-only; null
/// values are omitted.
/// </summary>
public sealed class DfeAnalyticsService(IEventSender eventSender) : IAnalyticsService
{
    public Task TrackAsync(AnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);

        var @event = eventSender.CreateEvent(analyticsEvent.EventType);

        foreach (var field in analyticsEvent.Fields)
        {
            if (field.Value is null) continue;

            // A string-list field becomes a multi-value DATA entry (BigQuery repeated field).
            if (field.Value is IEnumerable<string> values)
            {
                var array = values.ToArray();
                if (array.Length == 0) continue;
                if (field.Hidden)
                    @event.AddHiddenData(field.Name, array);
                else
                    @event.AddData(field.Name, array);
                continue;
            }

            var value = Stringify(field.Value);
            if (field.Hidden)
                @event.AddHiddenData(field.Name, value);
            else
                @event.AddData(field.Name, value);
        }

        return eventSender.SendEventAsync(@event);
    }

    private static string Stringify(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
