using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

/// <summary>
/// The results-enquiry slice of ingress (#324): several supplier CSVs -> one merged blob per
/// school, each row stamped with the tag of the file it came from -> StudentResultsBlobClient, the
/// client the enquiry journey itself reads through. Until this existed nothing but the dev-only
/// SeedStudentResults wrote that blob, so the journey had nothing to show on a deployed
/// environment.
/// </summary>
[Collection(nameof(AzuriteCollection))]
public sealed class ResultsEnquiryIngressTests(AzuriteFixture fixture)
{
    private const string Laestab = "860/4070";

    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    // The output contract (AB#296999). SOURCE is declared but never supplied by the CSV — the run
    // stamps it from the dataset slot, exactly as INCLUDED is stamped on 16-19 pupils.
    private const string Schema = """
    {
      "type": "object",
      "properties": {
        "CYPMD_ID":  { "type": ["string", "null"] },
        "QAN":       { "type": ["string", "null"] },
        "QUAL_NAME": { "type": ["string", "null"] },
        "SYLLABUS":  { "type": ["string", "null"] },
        "SESSION":   { "type": ["string", "null"] },
        "GRADE":     { "type": ["string", "null"] },
        "SOURCE":    { "type": "string" }
      }
    }
    """;

    private const string Header = "CYPMD_ID,QAN,QUAL_NAME,SYLLABUS,SESSION,GRADE,LAESTAB\n";

    private const string MainCsv = Header +
        "500001,6037116X,GCSE (9-1) Bus. Studs:Single,1BS0,S2024,5,8604070\n" +
        "500002,60181576,GCSE (9-1) French,1FR0,S2024,3,8604070\n";

    private const string LateResultsCsv = Header +
        "500001,60181576,GCSE (9-1) French,1FR0,S2024,6,8604070\n";

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IReadOnlyList<IngressDataset> Datasets() =>
    [
        new(ResultsFileTags.Post16Main, "main.csv", Checksum(MainCsv), "results.json", Checksum(Schema),
            Included: null, SourceFile: ResultsFileTags.Post16Main),
        new(ResultsFileTags.Post16LateResults1, "lr1.csv", Checksum(LateResultsCsv), "results.json", Checksum(Schema),
            Included: null, SourceFile: ResultsFileTags.Post16LateResults1)
    ];

    private async Task<Guid> SeedWindowAsync(params (string Path, string Content)[] files)
    {
        var windowId = Guid.NewGuid();
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var (path, content) in files.Length > 0
                     ? files
                     : [("ingress/main.csv", MainCsv), ("ingress/lr1.csv", LateResultsCsv), ("schema/results.json", Schema)])
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        return windowId;
    }

    private async Task<ValidationProgress> RunAsync(Guid windowId, IReadOnlyList<IngressDataset>? datasets = null)
    {
        var processor = new CsvSchemaFileProcessor(
            NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = _blobs });

        ValidationProgress? last = null;
        await foreach (var progress in processor.ProcessAsync(
                           windowId, CheckingExerciseType.ResultsEnquiry, datasets ?? Datasets(),
                           clearExistingFiles: true))
        {
            last = progress;
        }

        Assert.NotNull(last);
        return last!;
    }

    private IStudentResultsClient ResultsClient() =>
        new StudentResultsBlobClient(_blobs, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task A_clean_run_writes_one_merged_file_the_enquiry_journey_can_read()
    {
        var windowId = await SeedWindowAsync();

        var last = await RunAsync(windowId);
        Assert.False(last.IsError, last.Message);

        // Read back through the journey's own client, so the blob name, the JSON shape and the
        // property names are all checked by the code that has to consume them.
        var results = await ResultsClient().GetResultsAsync(windowId, Laestab, "500001");

        Assert.Equal(2, results.Count);
        var main = Assert.Single(results, r => r.SourceFile == ResultsFileTags.Post16Main);
        Assert.Equal("6037116X", main.Qan);
        Assert.Equal("GCSE (9-1) Bus. Studs:Single", main.QualificationName);
        Assert.Equal("1BS0", main.SyllabusCode);
        Assert.Equal("S2024", main.Session);
        Assert.Equal("5", main.Grade);
    }

    [Fact]
    public async Task Every_row_is_stamped_with_the_tag_of_the_file_it_came_from()
    {
        // Provenance by file of origin. The result picker's file column and
        // ILateResultsAvailability both read this, and no CSV supplies it.
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        Assert.True(await ResultsClient().AnyForSourceAsync(windowId, Laestab, ResultsFileTags.Post16Main));
        Assert.True(await ResultsClient().AnyForSourceAsync(windowId, Laestab, ResultsFileTags.Post16LateResults1));
        Assert.False(await ResultsClient().AnyForSourceAsync(windowId, Laestab, ResultsFileTags.Post16LateResults2));
    }

    [Fact]
    public async Task A_students_late_result_joins_their_main_results_rather_than_replacing_them()
    {
        // Both files carry the same student and the same QAN, distinguished only by source. One
        // file per school means the merge happens in the blob, not in the reader.
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        var results = await ResultsClient().GetResultsAsync(windowId, Laestab, "500001");

        Assert.Equal(2, results.Select(r => r.CompositeKey).Distinct().Count());
        Assert.Equal("6", results.Single(r => r.SourceFile == ResultsFileTags.Post16LateResults1).Grade);
    }

    [Fact]
    public async Task The_run_writes_the_results_name_and_leaves_no_pupil_blob_behind()
    {
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        Assert.True((await container
            .GetBlobClient(CheckingExerciseBlobPaths.ResultsBlobName(Laestab)).ExistsAsync()).Value);
        Assert.False((await container
            .GetBlobClient(CheckingExerciseBlobPaths.PupilsBlobName(CheckingExerciseType.PupilData, Laestab))
            .ExistsAsync()).Value);
    }

    [Fact]
    public async Task A_file_with_no_LAESTAB_column_fails_the_run_by_name()
    {
        // The rows cannot be split into schools at all, so the run has to stop. Naming the file is
        // the whole value of the message: six files are uploaded and one of them is wrong.
        const string noLaestab = "CYPMD_ID,QAN,QUAL_NAME,SYLLABUS,SESSION,GRADE\n500001,6037116X,Bus,1BS0,S2024,5\n";
        var windowId = await SeedWindowAsync(
            ("ingress/main.csv", noLaestab), ("schema/results.json", Schema));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Main, "main.csv", Checksum(noLaestab), "results.json", Checksum(Schema),
                Included: null, SourceFile: ResultsFileTags.Post16Main)
        ]);

        Assert.True(last.IsError);
        Assert.Contains("main.csv", last.Message);
        Assert.Contains("LAESTAB", last.Message);
    }

    [Fact]
    public async Task Nothing_is_stamped_when_the_schema_has_no_SOURCE_column()
    {
        // The stamp is guarded by the schema, so a dataset carrying a tag cannot break a run whose
        // schema forbids additional properties — the same guard INCLUDED has.
        const string schemaWithoutSource = """
        {
          "type": "object",
          "properties": {
            "CYPMD_ID": { "type": ["string", "null"] },
            "GRADE":    { "type": ["string", "null"] }
          }
        }
        """;
        var windowId = await SeedWindowAsync(
            ("ingress/main.csv", MainCsv), ("schema/results.json", schemaWithoutSource));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Main, "main.csv", Checksum(MainCsv), "results.json",
                Checksum(schemaWithoutSource), Included: null, SourceFile: ResultsFileTags.Post16Main)
        ]);

        Assert.False(last.IsError, last.Message);

        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        var content = (await container
            .GetBlobClient(CheckingExerciseBlobPaths.ResultsBlobName(Laestab)).DownloadContentAsync())
            .Value.Content.ToString();

        Assert.DoesNotContain("SOURCE", content, StringComparison.Ordinal);
        Assert.Equal(2, JsonDocument.Parse(content).RootElement.GetArrayLength());
    }
}
