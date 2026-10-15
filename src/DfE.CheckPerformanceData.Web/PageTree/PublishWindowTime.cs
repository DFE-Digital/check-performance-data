using System.Globalization;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// Converts page-version publish windows between the UK time editors type into, and read from,
/// the <c>datetime-local</c> fields and the UTC instants that are stored and compared against
/// the clock. See <see cref="LondonTime.TryLondonToUtc"/> for how the clock-change hours resolve.
/// </summary>
public static class PublishWindowTime
{
    /// <summary>The value format a <c>datetime-local</c> input reads and posts.</summary>
    public const string InputFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>Formats a stored UTC instant as UK time for a <c>datetime-local</c> input.</summary>
    public static string ToInputValue(DateTime? utc) => utc.ToLondonString(InputFormat);

    /// <summary>
    /// Reads a posted <c>datetime-local</c> value as UK time. A blank value is an open window
    /// (<paramref name="utc"/> is null). Returns <c>false</c> with a GOV.UK-style
    /// <paramref name="error"/> naming <paramref name="fieldLabel"/> when the value cannot be read
    /// or is a time that does not exist in the UK.
    /// </summary>
    public static bool TryParse(string? value, string fieldLabel, out DateTime? utc, out string? error)
    {
        utc = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var typed))
        {
            error = $"{fieldLabel} must be a real date and time";
            return false;
        }

        if (!LondonTime.TryLondonToUtc(typed, out var resolved))
        {
            error = $"{fieldLabel} must be a real UK time. On " +
                    $"{typed.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} the clocks go forward " +
                    $"from 1am to 2am, so {typed.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant()} " +
                    "does not exist";
            return false;
        }

        utc = resolved;
        return true;
    }
}
