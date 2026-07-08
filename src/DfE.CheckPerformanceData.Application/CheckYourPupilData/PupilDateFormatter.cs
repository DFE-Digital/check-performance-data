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
}
