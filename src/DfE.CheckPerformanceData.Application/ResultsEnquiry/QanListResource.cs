namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads a QAN list from an embedded CSV file with the columns <c>QAN,QualificationTitle</c>.
/// </summary>
internal static class QanListResource
{
    /// <summary>
    /// The QANs in the embedded resource whose name ends with <paramref name="resourceSuffix"/>.
    /// The set ignores case.
    /// </summary>
    public static IReadOnlySet<string> Load(string resourceSuffix)
    {
        var assembly = typeof(QanListResource).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        var qans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        reader.ReadLine(); // The header row.
        while (reader.ReadLine() is { } line)
        {
            // The QAN is the first column. It has no comma, so the quoted title does not matter.
            var qan = line.Split(',', 2)[0].Trim();
            if (qan.Length > 0) qans.Add(qan);
        }

        return qans;
    }
}
