using System.Globalization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Normalises a supplier pupil date (<c>DOB</c> / <c>ENTRYDAT</c>) to the UK display form
/// <c>dd/MM/yyyy</c>. These arrive as raw strings whose format varies: the schema promises ISO
/// <c>yyyy-MM-dd</c>, but sample data has full timestamps (<c>yyyy-MM-dd HH:mm:ss.fffffff</c>), and
/// the dev seed writes <c>dd/MM/yyyy</c>. Anything that does not match a known shape is returned
/// unchanged so we never silently drop or corrupt a value we don't recognise.
/// </summary>
public static class PupilDateFormatter
{
    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss.fffffff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "dd/MM/yyyy",
    ];

    public static string ToDisplayDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw ?? string.Empty;

        return DateTime.TryParseExact(raw.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : raw;
    }

    /// <summary>
    /// Normalises a supplier pupil date (<c>DOB</c> / <c>ENTRYDAT</c>) to ISO
    /// <c>yyyy-MM-dd</c> — the form the Zendesk Admission date field expects. Accepts the
    /// same supplier shapes as <see cref="ToDisplayDate"/>; anything unrecognised is returned
    /// unchanged so a value we don't recognise is never silently dropped.
    /// </summary>
    public static string ToIsoDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw ?? string.Empty;

        return DateTime.TryParseExact(raw.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : raw;
    }

    /// <summary>
    /// Like <see cref="ToIsoDate"/>, but signals parse failure instead of returning the raw
    /// string. Returns <c>false</c> when the value is null, empty, or matches no accepted
    /// supplier shape — the caller must then omit the field (e.g. a Zendesk admission date
    /// that must never be sent raw, which would reject the whole ticket).
    /// </summary>
    public static bool TryToIsoDate(string? raw, out string isoDate)
    {
        isoDate = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!DateTime.TryParseExact(raw.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return false;

        isoDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }
}
