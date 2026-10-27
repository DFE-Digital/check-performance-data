using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

/// <summary>
/// Renders an already-projected <see cref="PupilTable"/> as CSV. The column set — and therefore
/// which window type's fields appear — is decided in Application by <c>PupilColumnSets</c>, so
/// the table on screen and the CSV can never drift apart.
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
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
