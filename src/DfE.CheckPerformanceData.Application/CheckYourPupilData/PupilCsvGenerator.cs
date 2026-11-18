using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Renders an already-projected <see cref="PupilTable"/> as CSV. The column set — and therefore
/// which window type's fields appear — is decided in Application by <c>PupilColumnSets</c>, so
/// the table on screen and the CSV can never drift apart. Each heading and each value goes
/// through <see cref="SpreadsheetFormula.Neutralise(string)"/>, so a spreadsheet does not read
/// one as a formula.
/// </summary>
public static class PupilCsvGenerator
{
    public static byte[] Generate(PupilTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Headers.Select(Escape)));

        foreach (var row in table.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        string text = SpreadsheetFormula.Neutralise(value);
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }
}
