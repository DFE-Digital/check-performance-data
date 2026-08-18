using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.IntegrationTests.ResultsEnquiry;

// AB#296648: the results blob client against real blob storage — the blob path (exercise-scoped
// per docs/16-19-window-model.md), the per-student filter, the source-tag probe that drives late
// results availability, and the caching that keeps a journey from re-downloading the file on
// every page.
[Collection(nameof(AzuriteCollection))]
public sealed class StudentResultsBlobClientTests(AzuriteFixture azurite)
{
    private const string Laestab = "8604070";

    private const string ResultsJson = """
    [
      { "CYPMD_ID": "1606464434", "QAN": "6037116X", "QUAL_NAME": "GCSE (9-1) Bus. Studs:Single",
        "SYLLABUS": "1BS0", "SESSION": "S2024", "GRADE": "5", "SOURCE": "16to19_MAIN" },
      { "CYPMD_ID": "1606464434", "QAN": "60181576", "QUAL_NAME": "GCSE (9-1) French",
        "SYLLABUS": "1FR0", "SESSION": "S2024", "GRADE": "7", "SOURCE": "16to19_LR1" },
      { "CYPMD_ID": "9999999999", "QAN": "60180882", "QUAL_NAME": "GCSE (9-1) Art&Des : Fine Art",
        "SYLLABUS": "1AD0", "SESSION": "S2024", "GRADE": "9", "SOURCE": "16to19_MAIN" }
    ]
    """;

    private static StudentResultsBlobClient NewClient(BlobServiceClient service)
        => new(service, new MemoryCache(new MemoryCacheOptions()));

    private async Task<(Guid WindowId, BlobServiceClient Service)> SeededWindowAsync(string json = ResultsJson)
    {
        var windowId = Guid.NewGuid();
        var service = new BlobServiceClient(azurite.ConnectionString);
        var container = service.GetBlobContainerClient(windowId.ToString());
        await container.CreateAsync();
        await container.UploadBlobAsync(
            ResultsEnquiryBlobPaths.ResultsBlobName(Laestab), BinaryData.FromString(json));
        return (windowId, service);
    }

    [Fact]
    public async Task GetResultsAsync_returns_only_the_requested_students_results()
    {
        var (windowId, service) = await SeededWindowAsync();

        var results = await NewClient(service).GetResultsAsync(windowId, Laestab, "1606464434");

        Assert.Equal(2, results.Count);
        Assert.Equal(["6037116X", "60181576"], results.Select(r => r.Qan).ToArray());
        Assert.All(results, r => Assert.Equal("1606464434", r.CypmdId));
    }

    [Fact]
    public async Task GetResultsAsync_reads_the_exercise_scoped_blob_path()
    {
        // Pinning the path proves the reader and the (future) per-exercise ingress agree.
        var (windowId, service) = await SeededWindowAsync();
        var blob = service.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient("results-enquiry/data/8604070_results.json");

        Assert.True(await blob.ExistsAsync());
        Assert.NotEmpty(await NewClient(service).GetResultsAsync(windowId, Laestab, "1606464434"));
    }

    [Fact]
    public async Task GetResultsAsync_normalises_a_laestab_that_is_not_digits_only()
    {
        var (windowId, service) = await SeededWindowAsync();

        var results = await NewClient(service).GetResultsAsync(windowId, "860/4070", "1606464434");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetResultsAsync_unknown_student_returns_empty()
    {
        var (windowId, service) = await SeededWindowAsync();

        Assert.Empty(await NewClient(service).GetResultsAsync(windowId, Laestab, "0000000000"));
    }

    [Fact]
    public async Task GetResultsAsync_missing_container_returns_empty()
    {
        var service = new BlobServiceClient(azurite.ConnectionString);

        Assert.Empty(await NewClient(service).GetResultsAsync(Guid.NewGuid(), Laestab, "1606464434"));
    }

    [Fact]
    public async Task GetResultsAsync_missing_blob_returns_empty()
    {
        var windowId = Guid.NewGuid();
        var service = new BlobServiceClient(azurite.ConnectionString);
        await service.GetBlobContainerClient(windowId.ToString()).CreateAsync();

        Assert.Empty(await NewClient(service).GetResultsAsync(windowId, Laestab, "1606464434"));
    }

    [Fact]
    public async Task GetResultsAsync_malformed_json_throws_so_a_corrupt_file_surfaces()
    {
        var (windowId, service) = await SeededWindowAsync("{not json");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => NewClient(service).GetResultsAsync(windowId, Laestab, "1606464434"));
    }

    [Theory]
    [InlineData(ResultsFileTags.Post16Main, true)]
    [InlineData(ResultsFileTags.Post16LateResults1, true)]
    [InlineData(ResultsFileTags.Post16LateResults2, false)]
    [InlineData(ResultsFileTags.Post16Revised, false)]
    public async Task AnyForSourceAsync_reports_whether_a_source_file_has_landed(string tag, bool expected)
    {
        var (windowId, service) = await SeededWindowAsync();

        Assert.Equal(expected, await NewClient(service).AnyForSourceAsync(windowId, Laestab, tag));
    }

    [Fact]
    public async Task AnyForSourceAsync_is_not_scoped_to_one_student()
    {
        // The Art&Des row belongs to a different student but still proves MAIN has landed.
        var (windowId, service) = await SeededWindowAsync("""
        [{ "CYPMD_ID": "9999999999", "QAN": "60180882", "SESSION": "S2024", "GRADE": "9", "SOURCE": "16to19_MAIN" }]
        """);

        Assert.True(await NewClient(service).AnyForSourceAsync(windowId, Laestab, ResultsFileTags.Post16Main));
    }

    [Fact]
    public async Task The_school_file_is_downloaded_once_and_then_served_from_cache()
    {
        // A journey reads the results file on the search page, the details page and the summary.
        // Overwriting the blob and still seeing the old value proves the second read never went
        // to storage — the same stale-read assertion the pupil cache tests make.
        var (windowId, service) = await SeededWindowAsync();
        var client = NewClient(service);

        Assert.Equal(2, (await client.GetResultsAsync(windowId, Laestab, "1606464434")).Count);

        await service.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(ResultsEnquiryBlobPaths.ResultsBlobName(Laestab))
            .UploadAsync(BinaryData.FromString("[]"), overwrite: true);

        Assert.Equal(2, (await client.GetResultsAsync(windowId, Laestab, "1606464434")).Count);
        // A different client instance has its own cache, so it sees the truth.
        Assert.Empty(await NewClient(service).GetResultsAsync(windowId, Laestab, "1606464434"));
    }

    [Fact]
    public async Task AnyForSourceAsync_shares_the_cached_file_with_GetResultsAsync()
    {
        var (windowId, service) = await SeededWindowAsync();
        var client = NewClient(service);

        await client.GetResultsAsync(windowId, Laestab, "1606464434");
        await service.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(ResultsEnquiryBlobPaths.ResultsBlobName(Laestab))
            .UploadAsync(BinaryData.FromString("[]"), overwrite: true);

        Assert.True(await client.AnyForSourceAsync(windowId, Laestab, ResultsFileTags.Post16Main));
    }

    [Fact]
    public async Task UploadResultsAsync_writes_a_readable_file_and_invalidates_the_cache()
    {
        var windowId = Guid.NewGuid();
        var service = new BlobServiceClient(azurite.ConnectionString);
        var client = NewClient(service);

        // Prime the cache with the empty state, as dev seeding would after a health-check read.
        Assert.Empty(await client.GetResultsAsync(windowId, Laestab, "1606464434"));

        await client.UploadResultsAsync(windowId, Laestab, [new StudentResultRecord
        {
            CypmdId = "1606464434",
            Qan = "6037116X",
            QualificationName = "GCSE (9-1) Bus. Studs:Single",
            SyllabusCode = "1BS0",
            Session = "S2024",
            Grade = "5",
            SourceFile = ResultsFileTags.Post16Main
        }]);

        var results = await client.GetResultsAsync(windowId, Laestab, "1606464434");

        var only = Assert.Single(results);
        Assert.Equal("6037116X", only.Qan);
        Assert.Equal("5", only.Grade);
        Assert.Equal(ResultsFileTags.Post16Main, only.SourceFile);
    }
}
