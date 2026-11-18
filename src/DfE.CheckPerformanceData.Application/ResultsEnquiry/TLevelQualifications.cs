namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The T Level qualifications that DCI gave for this year, by QAN. The results enquiry ticket sets
/// "T Level Exams (CYPMD)" from this list (#859). The list is <c>Data/t-level-qans.csv</c>, an
/// embedded resource. When DCI updates the list, update the CSV file.
/// </summary>
public static class TLevelQualifications
{
    private const string ResourceSuffix = "ResultsEnquiry.Data.t-level-qans.csv";

    private static readonly Lazy<IReadOnlySet<string>> Qans = new(Load);

    /// <summary>True when the QAN is on the T Level list. The test ignores case and spaces.</summary>
    public static bool Contains(string? qan) =>
        !string.IsNullOrWhiteSpace(qan) && Qans.Value.Contains(qan.Trim());

    /// <summary>Every QAN on the list.</summary>
    public static IReadOnlySet<string> All => Qans.Value;

    private static IReadOnlySet<string> Load()
    {
        var assembly = typeof(TLevelQualifications).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
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
