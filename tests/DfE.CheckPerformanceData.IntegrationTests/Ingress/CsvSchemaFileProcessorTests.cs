using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

// The 16-19 supplier delivers TWO pupil CSVs per window (included + non-included) and the
// non-included file has no P_INCL column, so inclusion is stamped from the file of origin.
// Both must land in ONE data/{laestab}_pupils.json per school within a SINGLE run, because
// a second run would overwrite the first's output.
[Collection(nameof(AzuriteCollection))]
public sealed class CsvSchemaFileProcessorTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private const string IncludedSchema = """
    {
      "type": "object",
      "properties": {
        "Id":       { "type": ["string", "null"] },
        "INCLUDED": { "type": "boolean" },
        "CYPMD_ID": { "type": ["string", "null"] },
        "SURNAME":  { "type": ["string", "null"] },
        "FORENAMES":{ "type": ["string", "null"] },
        "LAESTAB":  { "type": ["string", "null"] },
        "ULN":      { "type": ["string", "null"] },
        "P_INCL":   { "type": ["integer", "null"] }
      }
    }
    """;

    private const string NonIncludedSchema = """
    {
      "type": "object",
      "properties": {
        "Id":       { "type": ["string", "null"] },
        "INCLUDED": { "type": "boolean" },
        "CYPMD_ID": { "type": ["string", "null"] },
        "SURNAME":  { "type": ["string", "null"] },
        "FORENAMES":{ "type": ["string", "null"] },
        "LAESTAB":  { "type": ["string", "null"] },
        "ULN":      { "type": ["string", "null"] },
        "CampID_0": { "type": ["string", "null"] }
      }
    }
    """;

    private const string IncludedCsv =
        "CYPMD_ID,SURNAME,FORENAMES,LAESTAB,ULN,P_INCL\n500001,Smith,Alice,8604070,9900000001,501\n";

    private const string NonIncludedCsv =
        "CYPMD_ID,SURNAME,FORENAMES,LAESTAB,ULN,CampID_0\n500002,Jones,Bob,8604070,9900000002,C0\n";

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private async Task<BlobContainerClient> SeedWindowAsync(Guid windowId, params (string Path, string Content)[] files)
    {
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var (path, content) in files)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        return container;
    }

    private CsvSchemaFileProcessor Processor() => new(
        NullLogger<CsvSchemaFileProcessor>.Instance,
        new Dictionary<string, BlobServiceClient> { ["app"] = _blobs });

    private static async Task<List<ValidationProgress>> DrainAsync(IAsyncEnumerable<ValidationProgress> stream)
    {
        List<ValidationProgress> all = [];
        await foreach (var p in stream) all.Add(p);
        return all;
    }

    private static async Task<JArray?> ReadPupilsAsync(BlobContainerClient container, string laestab)
    {
        var blob = container.GetBlobClient($"data/{laestab}_pupils.json");
        if (!await blob.ExistsAsync()) return null;
        var content = await blob.DownloadContentAsync();
        return JArray.Parse(content.Value.Content.ToString());
    }

    [Fact]
    public async Task Merges_two_datasets_into_one_blob_per_school_marking_each_population()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("ingress/included.csv", IncludedCsv),
            ("ingress/nonincluded.csv", NonIncludedCsv),
            ("schema/included.json", IncludedSchema),
            ("schema/nonincluded.json", NonIncludedSchema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("included", "included.csv", Checksum(IncludedCsv), "included.json", Checksum(IncludedSchema), Included: true),
            new("nonincluded", "nonincluded.csv", Checksum(NonIncludedCsv), "nonincluded.json", Checksum(NonIncludedSchema), Included: false)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets));

        var last = progress[^1];
        Assert.False(last.IsError);
        Assert.Equal(2, last.RecordsRead);

        var pupils = await ReadPupilsAsync(container, "8604070");
        Assert.NotNull(pupils);
        Assert.Equal(2, pupils!.Count);

        var alice = pupils.Children<JObject>().Single(p => p["SURNAME"]!.Value<string>() == "Smith");
        var bob = pupils.Children<JObject>().Single(p => p["SURNAME"]!.Value<string>() == "Jones");

        Assert.True(alice["INCLUDED"]!.Value<bool>());
        Assert.False(bob["INCLUDED"]!.Value<bool>());
        Assert.Equal(501, alice["P_INCL"]!.Value<int>());
        Assert.Null(bob["P_INCL"]);          // not in the non-included schema
        Assert.Equal("C0", bob["CampID_0"]!.Value<string>());
        Assert.NotEqual(Guid.Empty, Guid.Parse(alice["Id"]!.Value<string>()!));
    }

    [Fact]
    public async Task A_pupils_id_is_always_generated_on_ingress_even_when_the_schema_and_file_disagree()
    {
        // A pupil's Id is ours, never the supplier's. The supplier's schema does not declare one,
        // and a file that does carry one must not set it.
        const string schema = """
        {
          "type": "object",
          "properties": {
            "CYPMD_ID": { "type": ["string", "null"] },
            "SURNAME":  { "type": ["string", "null"] },
            "LAESTAB":  { "type": ["string", "null"] }
          }
        }
        """;
        const string supplied = "11111111-1111-1111-1111-111111111111";
        var csv = $"Id,CYPMD_ID,SURNAME,LAESTAB\n{supplied},500001,Smith,8604070\n{supplied},500002,Jones,8604070\n";
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId, ("ingress/pupils.csv", csv), ("schema/pupils.json", schema));

        var progress = await DrainAsync(Processor().ProcessAsync(windowId, CheckingExerciseType.PupilData,
            [new IngressDataset("pupils", "pupils.csv", Checksum(csv), "pupils.json", Checksum(schema), Included: null)]));

        Assert.False(progress[^1].IsError);
        var ids = (await ReadPupilsAsync(container, "8604070"))!.Children<JObject>()
            .Select(p => Guid.Parse(p["Id"]!.Value<string>()!)).ToList();
        Assert.Equal(2, ids.Count);
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.DoesNotContain(Guid.Parse(supplied), ids);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task An_error_in_either_dataset_writes_nothing()
    {
        var windowId = Guid.NewGuid();
        // CampID_0 is declared integer-only here, so the string value in the CSV fails validation.
        const string strictSchema = """
        {
          "type": "object",
          "properties": {
            "Id":       { "type": ["string", "null"] },
            "INCLUDED": { "type": "boolean" },
            "SURNAME":  { "type": ["string", "null"] },
            "FORENAMES":{ "type": ["string", "null"] },
            "CYPMD_ID": { "type": ["string", "null"] },
            "LAESTAB":  { "type": ["string", "null"] },
            "CampID_0": { "type": "integer" }
          }
        }
        """;

        var container = await SeedWindowAsync(windowId,
            ("ingress/included.csv", IncludedCsv),
            ("ingress/nonincluded.csv", NonIncludedCsv),
            ("schema/included.json", IncludedSchema),
            ("schema/strict.json", strictSchema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("included", "included.csv", Checksum(IncludedCsv), "included.json", Checksum(IncludedSchema), Included: true),
            new("nonincluded", "nonincluded.csv", Checksum(NonIncludedCsv), "strict.json", Checksum(strictSchema), Included: false)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets));

        var last = progress[^1];
        Assert.True(last.IsError);
        Assert.Null(await ReadPupilsAsync(container, "8604070"));
    }

    // A KS4 schema declares no INCLUDED property at all — inclusion comes from the record's own
    // P_INCL. This is the shape every pre-16-19 window uses.
    private const string Ks4Schema = """
    {
      "type": "object",
      "properties": {
        "Id":       { "type": ["string", "null"] },
        "CYPMD_ID": { "type": ["string", "null"] },
        "SURNAME":  { "type": ["string", "null"] },
        "FORENAMES":{ "type": ["string", "null"] },
        "LAESTAB":  { "type": ["string", "null"] },
        "ULN":      { "type": ["string", "null"] },
        "P_INCL":   { "type": ["integer", "null"] }
      }
    }
    """;

    [Fact]
    public async Task A_single_ks4_dataset_run_is_unchanged_and_stamps_nothing()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("ingress/pupils.csv", IncludedCsv),
            ("schema/ks4.json", Ks4Schema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("pupils", "pupils.csv", Checksum(IncludedCsv), "ks4.json", Checksum(Ks4Schema), Included: null)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets));

        Assert.False(progress[^1].IsError);

        var pupils = await ReadPupilsAsync(container, "8604070");
        var alice = Assert.Single(pupils!.Children<JObject>());
        // The stamp is guarded by schema.Properties.ContainsKey("INCLUDED"), so a KS4 file never
        // gains the marker and its inclusion stays driven by P_INCL alone.
        Assert.Null(alice["INCLUDED"]);
        Assert.Equal(501, alice["P_INCL"]!.Value<int>());
    }

    [Fact]
    public async Task A_dataset_with_no_marker_does_not_claim_inclusion_when_the_schema_declares_it()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("ingress/included.csv", IncludedCsv),
            ("schema/included.json", IncludedSchema));

        // Misconfiguration guard: the schema declares INCLUDED but the dataset supplies no
        // marker, so nothing is stamped and the boolean field cannot be satisfied. The run must
        // fail loudly rather than silently writing pupils with an unset inclusion.
        IReadOnlyList<IngressDataset> datasets =
        [
            new("pupils", "included.csv", Checksum(IncludedCsv), "included.json", Checksum(IncludedSchema), Included: null)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets));

        Assert.True(progress[^1].IsError);
        Assert.Null(await ReadPupilsAsync(container, "8604070"));
    }

    private static async Task<List<string>> ListBlobNamesAsync(BlobContainerClient container)
    {
        List<string> names = [];
        await foreach (var blob in container.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }
        return names;
    }

    [Fact]
    public async Task ARunForAnExercise_WritesUnderThatExercisesPrefix()
    {
        var windowId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("ingress/pupils.csv", IncludedCsv),
            ("schema/ks4.json", Ks4Schema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("pupils", "pupils.csv", Checksum(IncludedCsv), "ks4.json", Checksum(Ks4Schema), Included: null)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(
            windowId, CheckingExerciseType.PupilData, datasets, checkingExerciseId: exerciseId));

        Assert.False(progress[^1].IsError);

        var writtenBlobNames = await ListBlobNamesAsync(container);

        Assert.Contains(writtenBlobNames, name =>
            name.StartsWith($"exercises/{exerciseId}/data/", StringComparison.Ordinal));
        Assert.DoesNotContain(writtenBlobNames, name => name.StartsWith("data/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARunWithNoExerciseId_KeepsWritingTheLegacyPaths()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("ingress/pupils.csv", IncludedCsv),
            ("schema/ks4.json", Ks4Schema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("pupils", "pupils.csv", Checksum(IncludedCsv), "ks4.json", Checksum(Ks4Schema), Included: null)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(
            windowId, CheckingExerciseType.PupilData, datasets, checkingExerciseId: null));

        Assert.False(progress[^1].IsError);

        var writtenBlobNames = await ListBlobNamesAsync(container);

        Assert.Contains(writtenBlobNames, name => name.StartsWith("data/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedRun_LeavesThePreviousOutputInPlace()
    {
        // Clearing before validation destroyed a school's good data whenever the new file was bad.
        var windowId = Guid.NewGuid();
        const string strictSchema = """
        {
          "type": "object",
          "properties": {
            "Id":       { "type": ["string", "null"] },
            "INCLUDED": { "type": "boolean" },
            "SURNAME":  { "type": ["string", "null"] },
            "FORENAMES":{ "type": ["string", "null"] },
            "CYPMD_ID": { "type": ["string", "null"] },
            "LAESTAB":  { "type": ["string", "null"] },
            "CampID_0": { "type": "integer" }
          }
        }
        """;

        var container = await SeedWindowAsync(windowId,
            ("data/8604070_pupils.json", "[{\"SURNAME\":\"Existing\"}]"),
            ("ingress/nonincluded.csv", NonIncludedCsv),
            ("schema/strict.json", strictSchema));

        IReadOnlyList<IngressDataset> datasets =
        [
            new("nonincluded", "nonincluded.csv", Checksum(NonIncludedCsv), "strict.json", Checksum(strictSchema), Included: false)
        ];

        var progress = await DrainAsync(Processor().ProcessAsync(
            windowId, CheckingExerciseType.PupilData, datasets, clearExistingFiles: true));

        Assert.True(progress[^1].IsError);

        var existingBlobNames = await ListBlobNamesAsync(container);
        Assert.Contains(existingBlobNames, name => name.StartsWith("data/", StringComparison.Ordinal));
    }
}
