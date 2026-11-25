using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Dev-only: the pupil data exercise of the Key Stage 2 window
/// (<see cref="SeedCheckingWindows.Ks2PupilsDatasets"/>). Links <c>pupils/pupils.csv</c> to the
/// September slot with <c>ks2/pupils_schema.json</c> and validates the exercise, so it has one
/// release. The December slot stays empty, as at the start of the window.
/// </summary>
/// <remarks>
/// The rows come from <see cref="Ks2SampleData"/>, which also gives sample data to the schools of a
/// window that use it (#939).
/// The seed also writes both samples to the ingress storage account, container
/// <see cref="SeedKs2Summary.Container"/>, for an admin to do the December step by hand: fill the
/// December slot, retire the September slot and validate.
/// <list type="bullet">
/// <item>September: <c>pupils/pupils.csv</c> → <c>pupils_schema.json</c></item>
/// <item>December: <c>pupils/pupils-december.csv</c> → <c>pupils-december_schema.json</c>, the
/// September columns plus <c>CLA_PP_1_DAY</c> and <c>FSM6CLA1A</c></item>
/// </list>
///
/// Each file has the supplier's column names, taken from the schema: a property read through
/// <c>x-ingress.source</c> is written under its source column (<c>PINCL_Flag</c>, <c>Sex</c>,
/// <c>SEN</c>, <c>ETHNICITY</c>), so ingress does the same renaming it will do for the real file.
/// <c>INCLUDED</c> and <c>P_INCL_DESC</c> are not written: ingress stamps them from the schema's
/// inclusion rule.
///
/// Each KS2 school (<see cref="SeedKs2Summary.Schools"/>) has <see cref="Ks2SampleData.Cohort"/> included pupils (201), of whom
/// <see cref="Ks2SampleData.Boys"/> are boys, so the pupil list agrees with the Summary tab. Each
/// school also has <see cref="Ks2SampleData.NonIncludedPerSchool"/> non-included pupils: 202 (not at the end of
/// KS2, so no KS2 results). Only the December file has 203 (excluded at the school's request, with
/// results): there every other non-included pupil is 203. The values are
/// made up but stable, and agree with each other: an outcome follows its scaled score, and a
/// progress score is the attainment score less the predicted score.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Development seed data, not product code.")]
public static class SeedKs2PupilData
{
    public const string SeptemberFile = "pupils/pupils.csv";
    public const string SeptemberSchema = "pupils_schema.json";
    public const string DecemberFile = "pupils/pupils-december.csv";
    public const string DecemberSchema = "pupils-december_schema.json";

    // Stamped by ingress from the schema's inclusion rule, never sent by the supplier.
    private static readonly HashSet<string> StampedByIngress = ["INCLUDED", "INCLUDABLE", "P_INCL_DESC"];

    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath)
    {
        var window = await SeedExerciseFixtures.LoadAsync(dbContext, DevDataSeeder.KeyStage2CheckingWindowId);
        var pupils = SeedExerciseFixtures.Exercise(window, CheckingExerciseType.PupilData);

        var schema = ReadSchema(contentRootPath, SeptemberSchema);
        await SeedExerciseFixtures.LinkAsync(blobs, pupils,
            pupils.Datasets.Single(d => d.Name == SeedCheckingWindows.Ks2PupilsDatasets[0]),
            Path.GetFileName(SeptemberFile), Csv(schema, december: false), SeptemberSchema, schema);
        await dbContext.SaveChangesAsync();
        await SeedExerciseFixtures.IngestAsync(blobs, ingress, pupils);
    }

    /// <summary>The sample files, by their path in <see cref="SeedKs2Summary.Container"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files(string contentRootPath) =>
        new Dictionary<string, byte[]>
        {
            [SeptemberFile] = Csv(ReadSchema(contentRootPath, SeptemberSchema), december: false),
            [DecemberFile] = Csv(ReadSchema(contentRootPath, DecemberSchema), december: true)
        };

    /// <summary>
    /// Writes the sample files, replacing any from an earlier seed. Does nothing when no ingress
    /// storage account is configured.
    /// </summary>
    public static Task WriteSamplesAsync(IReadOnlyDictionary<string, BlobServiceClient> blobClients,
        string contentRootPath, ILogger logger) =>
        SeedPost16OctoberSamples.WriteToIngressAsync(blobClients, SeedKs2Summary.Container, Files(contentRootPath),
            "Key Stage 2", logger);

    private static string ReadSchema(string contentRootPath, string schema) =>
        File.ReadAllText(Path.Combine(contentRootPath, "Data", "Ingress", "ks2", schema));

    // One row per pupil, one column per supplier column of the schema, in schema order.
    private static byte[] Csv(string schema, bool december)
    {
        using var doc = JsonDocument.Parse(schema);
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Where(p => !StampedByIngress.Contains(p.Name))
            .Select(p => (Property: p.Name, Header: SourceColumn(p.Value) ?? p.Name))
            .ToList();

        var rows = SeedKs2Summary.Schools.SelectMany((school, index) => Ks2SampleData.Pupils(school.Urn, school.Laestab, index, december));
        return SeedExerciseFixtures.WriteCsv(rows.Select(values =>
            (IReadOnlyDictionary<string, string>)columns.ToDictionary(c => c.Header, c => values.TryGetValue(c.Property, out var value)
                ? value
                : throw new InvalidOperationException($"The KS2 pupil seed has no value for column '{c.Property}'."))));
    }

    private static string? SourceColumn(JsonElement property) =>
        property.TryGetProperty("x-ingress", out var ingress) && ingress.TryGetProperty("source", out var source) &&
        source.ValueKind == JsonValueKind.String
            ? source.GetString()
            : null;
}
