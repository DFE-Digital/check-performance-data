using System.Text;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// Writes an LDS file: RFC 4180 quoting, CRLF between lines, no line terminator after the final
/// data row ("no additional rows below the final data row"), every value trimmed ("no trailing
/// blank characters"), UTF-8 without a byte-order mark. Deliberately not PupilCsvGenerator, whose
/// AppendLine emits the host platform's line ending and a trailing newline.
/// </summary>
public static class EgressCsvWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Write<T>(IReadOnlyList<EgressColumn<T>> columns, IReadOnlyList<T> rows)
    {
        var lines = new List<string>(rows.Count + 1)
        {
            string.Join(",", columns.Select(c => Escape(c.Header)))
        };
        lines.AddRange(rows.Select(row => string.Join(",", columns.Select(c => Escape(c.Value(row))))));
        return Utf8NoBom.GetBytes(string.Join("\r\n", lines));
    }

    private static string Escape(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{trimmed.Replace("\"", "\"\"")}\""
            : trimmed;
    }
}
