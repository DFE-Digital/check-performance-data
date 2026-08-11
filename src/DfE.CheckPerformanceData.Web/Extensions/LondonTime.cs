using System.Globalization;
using DfE.CheckPerformanceData.Domain.Time;

namespace DfE.CheckPerformanceData.Web.Extensions;

/// <summary>
/// Presentation helper that converts stored UTC instants to UK local time
/// (Europe/London — BST in summer, GMT in winter) for display.
///
/// Datetimes are stored as UTC throughout the app and converted here at the
/// point of display. Window boundaries (<c>CheckingWindow.StartDate</c>/<c>EndDate</c>)
/// are the deliberate exception — they are wall-clock UK deadlines, not captured
/// instants, and are never routed through this helper.
/// </summary>
public static class LondonTime
{
    /// <summary>The Europe/London zone. Single definition, shared with the domain's
    /// <see cref="UkTime"/> so display and window-boundary logic can never diverge.</summary>
    public static readonly TimeZoneInfo Zone = UkTime.Zone;

    /// <summary>Friendly format for service-user views, e.g. "24 Jun 2026 13:05".</summary>
    public const string Friendly = "d MMM yyyy HH:mm";

    /// <summary>Precise format for ops/admin dashboards, e.g. "2026-06-24 13:05:00".</summary>
    public const string Precise = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Converts an instant to London local time. <see cref="DateTimeKind.Utc"/> and
    /// <see cref="DateTimeKind.Unspecified"/> are both treated as UTC (the latter because
    /// <c>timestamp without time zone</c> columns and JSON round-trips surface UTC values
    /// with an Unspecified kind); <see cref="DateTimeKind.Local"/> is normalised via UTC.
    /// </summary>
    public static DateTime ToLondon(DateTime instant)
    {
        var utc = instant.Kind switch
        {
            DateTimeKind.Utc => instant,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
            _ => instant.ToUniversalTime(),
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    /// <summary>
    /// Interprets a wall-clock value the user typed as London local time and returns the
    /// equivalent UTC instant. Used at the controller boundary for date/time filter inputs.
    /// </summary>
    public static DateTime LondonToUtc(DateTime wallClock)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    /// <summary>
    /// Formats an instant as the "Submitted by → When" display, e.g.
    /// "24 June 2026 at 1:05pm" in London local time. Empty string when null.
    /// </summary>
    public static string ToSubmittedAtText(DateTime? instant)
    {
        if (instant is not { } d) return string.Empty;
        var london = ToLondon(d);
        return $"{london.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} at " +
               $"{london.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant()}";
    }

    /// <summary>Converts to London and formats with the invariant culture.</summary>
    public static string ToLondonString(this DateTime instant, string format) =>
        ToLondon(instant).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Nullable overload — returns an empty string when there is no value.</summary>
    public static string ToLondonString(this DateTime? instant, string format) =>
        instant is { } d ? d.ToLondonString(format) : string.Empty;
}
