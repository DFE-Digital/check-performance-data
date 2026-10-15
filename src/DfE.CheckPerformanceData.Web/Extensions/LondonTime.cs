using System.Globalization;

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
    /// <summary>
    /// The Europe/London zone. The IANA id resolves on the Linux deploy target and,
    /// via .NET's ICU mapping, on Windows too. Resolved once at type load.
    /// </summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

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
    /// Interprets a wall-clock value the user typed as London local time and resolves it to a
    /// single UTC instant, settling the two clock-change cases explicitly:
    /// <list type="bullet">
    /// <item>A time in the hour skipped when the clocks go forward (late March, 1:00am to
    /// 1:59am) does not exist in the UK, so the method returns <c>false</c> and the caller asks
    /// the user for a real time rather than guessing which one they meant.</item>
    /// <item>A time in the hour repeated when the clocks go back (late October, 1:00am to
    /// 1:59am) resolves to its first occurrence, which is still British Summer Time.</item>
    /// </list>
    /// The kind of <paramref name="wallClock"/> is ignored: it is always read as London time.
    /// </summary>
    public static bool TryLondonToUtc(DateTime wallClock, out DateTime utc)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(unspecified))
        {
            utc = default;
            return false;
        }

        if (Zone.IsAmbiguousTime(unspecified))
        {
            // The first occurrence is the one with the larger offset from UTC (BST, UTC+1).
            var firstOccurrenceOffset = Zone.GetAmbiguousTimeOffsets(unspecified).Max();
            utc = DateTime.SpecifyKind(unspecified - firstOccurrenceOffset, DateTimeKind.Utc);
            return true;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
        return true;
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
