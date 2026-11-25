using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the summary data share of the Key Stage 2 window
/// (<see cref="SeedCheckingWindows.Ks2SummaryDatasets"/>). Links <c>summary/summary.csv</c> to the
/// September slot with <c>ks2/summary_schema.json</c> and validates the share, so it has one
/// release. The December slot stays empty, as at the start of the window.
/// </summary>
/// <remarks>
/// <para>
/// The seed also writes both samples to the ingress storage account, container
/// <see cref="Container"/>, for an admin to do the December step by hand: fill the December slot,
/// retire the September slot and validate.
/// </para>
/// <list type="bullet">
/// <item>September: <c>summary/summary.csv</c> → <c>summary_schema.json</c></item>
/// <item>December: <c>summary/summary-december.csv</c> → <c>summary-december_schema.json</c>, the
/// September columns plus the four disadvantaged pupil fields</item>
/// </list>
/// <para>
/// Each file has every column of its schema, one row per KS2 school (<see cref="Schools"/>). The
/// values are made up but stable, and agree with each other: boys and girls add up to the cohort,
/// and each percentage is its count over the cohort. A percentage has a % sign, as the
/// specification's format "999%" shows it. When there is more than one school, the last has too
/// few disadvantaged pupils to publish, so its disadvantaged percentages are suppressed ("+").
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedKs2Summary
{
    public const string Container = "ks2";

    public const string SeptemberFile = "summary/summary.csv";
    public const string SeptemberSchema = "summary_schema.json";
    public const string DecemberFile = "summary/summary-december.csv";
    public const string DecemberSchema = "summary-december_schema.json";

    /// <summary>
    /// The schools with KS2 data, the LAESTAB as the supplier sends it (no "/"). Five Ways Primary
    /// School, Heath Hayes (Staffordshire), and Kingsmead School. Kingsmead is a secondary school
    /// and would never see a KS2 window, but the dev impersonation signs in as Kingsmead, so it has
    /// data in every window. Kingsmead is last on purpose: the December sample suppresses the last
    /// school's disadvantaged percentages ("+"), so a developer can see that form on the Summary tab.
    /// </summary>
    internal static readonly IReadOnlyList<(string Urn, string Laestab)> Schools =
        [("124070", "8602180"), ("142313", "8604070")];

    /// <summary>The sample files, by their path in <see cref="Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files(string contentRootPath) =>
        new Dictionary<string, byte[]>
        {
            [SeptemberFile] = Csv(ReadSchema(contentRootPath, SeptemberSchema)),
            [DecemberFile] = Csv(ReadSchema(contentRootPath, DecemberSchema))
        };

    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, DevDataSeeder.KeyStage2CheckingWindowId);
        var summary = window.CheckingExercises.Single(e =>
            e.ExerciseType is null && e.Name == SeedCheckingWindows.Ks2SummaryExercise);

        var schema = ReadSchema(contentRootPath, SeptemberSchema);
        await SeedExerciseFixtures.LinkAsync(blobs, summary,
            summary.Datasets.Single(d => d.Name == SeedCheckingWindows.Ks2SummaryDatasets[0]),
            Path.GetFileName(SeptemberFile), Csv(schema), SeptemberSchema, schema);
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, summary);
    }

    /// <summary>
    /// Writes the sample files, replacing any from an earlier seed. Does nothing when no ingress
    /// storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients,
        string contentRootPath, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, Container, Files(contentRootPath),
            "Key Stage 2", logger);

    private static string ReadSchema(string contentRootPath, string schema) =>
        File.ReadAllText(Path.Combine(contentRootPath, "Data", "Ingress", "ks2", schema));

    // One row per school, with a value for every column of the schema, in schema order.
    private static byte[] Csv(string schema)
    {
        using var doc = JsonDocument.Parse(schema);
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        var schools = Schools;
        return SeedExerciseFixtures.WriteCsv(schools.Select((school, index) =>
        {
            var values = Ks2SampleData.Summary(school.Urn, school.Laestab, index, suppressDisadvantaged: schools.Count > 1 && index == schools.Count - 1);
            return (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c, c => values.TryGetValue(c, out var value)
                ? value
                : throw new InvalidOperationException($"The KS2 summary seed has no value for column '{c}'."));
        }));
    }
}
