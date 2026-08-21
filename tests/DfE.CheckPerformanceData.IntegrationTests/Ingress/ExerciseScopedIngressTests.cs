using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

// #316: two exercises share one {windowId} container, so an ingress run has to belong to an
// exercise rather than to the window. The failure this guards is the clear sweep: it used to delete
// everything under "data/" and every "{windowId}_summary_" blob, which meant a results-enquiry run
// destroyed the pupil-data output and forced a re-upload of data that was never at fault.
[Collection(nameof(AzuriteCollection))]
public sealed class ExerciseScopedIngressTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private const string Schema = """
    {
      "type": "object",
      "properties": {
        "Id":       { "type": ["string", "null"] },
        "INCLUDED": { "type": "boolean" },
        "CYPMD_ID": { "type": ["string", "null"] },
        "SURNAME":  { "type": ["string", "null"] },
        "LAESTAB":  { "type": ["string", "null"] }
      }
    }
    """;

    private const string Csv = "CYPMD_ID,SURNAME,LAESTAB\n500001,Smith,8604070\n";

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IReadOnlyList<IngressDataset> Datasets() =>
        [new("pupils", "pupils.csv", Checksum(Csv), "pupils.json", Checksum(Schema), Included: true)];

    private async Task<BlobContainerClient> SeedWindowAsync(Guid windowId,
        params (string Path, string Content)[] extra)
    {
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var (path, content) in extra.Concat(
                     [("ingress/pupils.csv", Csv), ("schema/pupils.json", Schema)]))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        return container;
    }

    private CsvSchemaFileProcessor Processor() => new(
        NullLogger<CsvSchemaFileProcessor>.Instance,
        new Dictionary<string, BlobServiceClient> { ["app"] = _blobs });

    private async Task RunAsync(Guid windowId, CheckingExerciseType exercise)
    {
        await foreach (var _ in Processor().ProcessAsync(
                           windowId, exercise, Datasets(), clearExistingFiles: true))
        {
        }
    }

    private static Task<bool> ExistsAsync(BlobContainerClient container, string name)
        => container.GetBlobClient(name).ExistsAsync().ContinueWith(t => t.Result.Value);

    [Fact]
    public async Task A_results_enquiry_run_leaves_the_pupil_data_blobs_and_summaries_alone()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("data/8604070_pupils.json", "[]"),
            ($"{windowId}_summary_20260101_120000.csv", "pupil data summary"),
            ($"{windowId}_error_log.txt", "pupil data errors"));

        await RunAsync(windowId, CheckingExerciseType.ResultsEnquiry);

        Assert.True(await ExistsAsync(container, "data/8604070_pupils.json"),
            "the results-enquiry sweep deleted the pupil-data output");
        Assert.True(await ExistsAsync(container, $"{windowId}_summary_20260101_120000.csv"),
            "the results-enquiry sweep deleted the pupil-data summary");
        Assert.True(await ExistsAsync(container, $"{windowId}_error_log.txt"),
            "the results-enquiry sweep deleted the pupil-data error log");
    }

    [Fact]
    public async Task A_pupil_data_run_leaves_the_results_enquiry_blobs_and_summaries_alone()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("results-enquiry/data/8604070_results.json", "[]"),
            ($"results-enquiry/{windowId}_summary_20260101_120000.csv", "results summary"),
            ($"results-enquiry/{windowId}_error_log.txt", "results errors"));

        await RunAsync(windowId, CheckingExerciseType.PupilData);

        Assert.True(await ExistsAsync(container, "results-enquiry/data/8604070_results.json"),
            "the pupil-data sweep deleted the results-enquiry output");
        Assert.True(await ExistsAsync(container, $"results-enquiry/{windowId}_summary_20260101_120000.csv"),
            "the pupil-data sweep deleted the results-enquiry summary");
        Assert.True(await ExistsAsync(container, $"results-enquiry/{windowId}_error_log.txt"),
            "the pupil-data sweep deleted the results-enquiry error log");
    }

    [Fact]
    public async Task A_run_still_clears_its_own_previous_output()
    {
        // Scoping the sweep must not turn it off: a re-run of one exercise still replaces that
        // exercise's own leftovers.
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId,
            ("data/9999999_pupils.json", "[]"),
            ($"{windowId}_summary_20260101_120000.csv", "stale summary"));

        await RunAsync(windowId, CheckingExerciseType.PupilData);

        Assert.False(await ExistsAsync(container, "data/9999999_pupils.json"),
            "a school that is no longer in the file was left behind");
        Assert.False(await ExistsAsync(container, $"{windowId}_summary_20260101_120000.csv"),
            "the previous run's summary was left behind");
    }

    [Fact]
    public async Task A_pupil_data_run_writes_to_the_same_paths_it_always_has()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId);

        await RunAsync(windowId, CheckingExerciseType.PupilData);

        Assert.True(await ExistsAsync(container, "data/8604070_pupils.json"));
    }

    [Fact]
    public async Task A_results_enquiry_run_writes_under_its_own_prefix()
    {
        var windowId = Guid.NewGuid();
        var container = await SeedWindowAsync(windowId);

        await RunAsync(windowId, CheckingExerciseType.ResultsEnquiry);

        // #324: the output is named for the exercise as well as prefixed by it — a results run
        // writes the name the enquiry journey's reader looks for.
        Assert.True(await ExistsAsync(container,
            CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.ResultsEnquiry, "8604070")));
        Assert.False(await ExistsAsync(container, "data/8604070_pupils.json"));
    }
}
