using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the summary data share every 16-19 window has (<see cref="SeedCheckingWindows.SummaryDatasets"/>).
/// Each step of the year fills its own slot, retires the slot before it and validates the share,
/// so each step makes a release. Each step has its own schema, whose section is the heading schools
/// see, so the heading names the step. The supplier sends three file shapes: November and February
/// have the same columns.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>October, "Summary": <c>summary/summary.csv</c> → <c>summary-autumn_schema.json</c></item>
/// <item>November, "Summary with value added": <c>summary/summary-value-added.csv</c> →
/// <c>summary-november-va_schema.json</c></item>
/// <item>February, "Summary with value added: revised": <c>summary/summary-value-added-revised.csv</c> →
/// <c>summary-november-va-revised_schema.json</c></item>
/// <item>March, "Summary with value added: revised including retention":
/// <c>summary/summary-value-added-revised-retention.csv</c> → <c>summary-retention_schema.json</c></item>
/// </list>
/// <para>
/// Each file has one row per school in the student files, and every column of its schema. The
/// values are made up but stable. A column that more than one file carries keeps its value from
/// October to November, and from February to March. The revised files change it.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedPost16Summary
{
    /// <summary>One step's summary file: its slot, sample file, schema, and whether the values are revised.</summary>
    public sealed record Step(string Slot, string File, string Schema, bool Revised);

    public static readonly Step October = new(SeedCheckingWindows.SummaryDatasets[0],
        "summary/summary.csv", "summary-autumn_schema.json", Revised: false);

    public static readonly Step November = new(SeedCheckingWindows.SummaryDatasets[1],
        "summary/summary-value-added.csv", "summary-november-va_schema.json", Revised: false);

    public static readonly Step February = new(SeedCheckingWindows.SummaryDatasets[2],
        "summary/summary-value-added-revised.csv", "summary-november-va-revised_schema.json", Revised: true);

    public static readonly Step March = new(SeedCheckingWindows.SummaryDatasets[3],
        "summary/summary-value-added-revised-retention.csv", "summary-retention_schema.json", Revised: true);

    public static readonly IReadOnlyList<Step> Steps = [October, November, February, March];

    /// <summary>The step's sample file, for the given window's schools.</summary>
    public static byte[] Csv(Step step, Guid windowId) =>
        SummaryCsv(SchemaColumns(step.Schema), windowId, step.Revised);

    /// <summary>
    /// What an admin does when a summary file lands: links it to its slot, makes the slot required,
    /// retires the slot before it and validates the share, which makes a new release. A window
    /// without the share (a test window) has nothing to fill.
    /// </summary>
    internal static async Task AddAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId, Step step)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, windowId);
        if (window.CheckingExercises.SingleOrDefault(e => e.ExerciseType is null && e.Name == SeedCheckingWindows.SummaryExercise)
            is not { } summary)
            return;

        var dataset = summary.Datasets.Single(d => d.Name == step.Slot);
        await SeedExerciseFixtures.LinkAsync(blobs, summary, dataset, Path.GetFileName(step.File),
            Csv(step, windowId), step.Schema,
            await File.ReadAllTextAsync(Path.Combine(contentRootPath, "Data", "Ingress", "post16", step.Schema)));
        dataset.Required = true;
        var index = Steps.ToList().IndexOf(step);
        if (index > 0)
            summary.Datasets.Single(d => d.Name == Steps[index - 1].Slot).Retired = true;
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, summary);
    }

    // The schema's properties, in column order. The schemas ship next to the app, so the sample
    // files can be written without a content root.
    internal static IReadOnlyList<string> SchemaColumns(string schema)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16", schema)));
        return doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
    }

    // One row per school in the student files, with a value for each column.
    internal static byte[] SummaryCsv(IReadOnlyList<string> columns, Guid windowId, bool revised)
    {
        var schools = SeedPupilData.Post16Pupils(windowId).Select(p => p.Laestab).Distinct().ToList();
        return SeedExerciseFixtures.WriteCsv(schools.Select((laestab, school) =>
            (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c, c => c switch
            {
                "LAESTAB" => laestab,
                "CampID" => string.Empty,
                _ => Value(c, school, revised)
            })));
    }

    private static readonly string[] Grades = ["A", "B+", "B", "B-", "C+", "C", "Merit+", "Merit", "Distinction-"];

    // A made-up value that has the column's shape: a grade, a point score, a percentage, a value
    // added score with its confidence interval, a progress score or a student count.
    private static string Value(string column, int school, bool revised)
    {
        var upper = column.ToUpperInvariant();
        var n = Seed(column, school, revised);
        if (upper.StartsWith("UCI_") || upper.StartsWith("LCI_"))
        {
            // The interval is either side of the value added score of the same measure.
            var score = ValueAdded(Seed("VA_" + column[4..], school, revised));
            return Format(upper.StartsWith("UCI_") ? score + 0.25m : score - 0.25m, "0.00");
        }
        if (upper.StartsWith("VA_")) return Format(ValueAdded(n), "0.00");
        if (upper.StartsWith("PROGEX")) return Format((n % 81 - 40) / 100m, "0.00");
        if (upper.Contains("GRD")) return Grades[n % Grades.Length];
        if (upper.StartsWith("TALLPPE") || upper == "TB3PTSE") return Format(20 + n % 250 / 10m, "0.00");
        if (upper.Contains("PER") || upper.StartsWith("PT")) return Format(40 + n % 600 / 10m, "0.0");
        return (5 + n % 200).ToString(CultureInfo.InvariantCulture);
    }

    private static decimal ValueAdded(int n) => (n % 61 - 30) / 100m;

    private static string Format(decimal value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    // Stable across runs, unlike string.GetHashCode.
    private static int Seed(string column, int school, bool revised)
    {
        var hash = column.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        return (hash & int.MaxValue) % 100_000 + school * 37 + (revised ? 11 : 0);
    }
}
