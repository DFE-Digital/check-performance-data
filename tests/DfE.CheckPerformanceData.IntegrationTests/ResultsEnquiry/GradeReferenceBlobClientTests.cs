using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.ResultsEnquiry;

// AB#297130: the grade reference against real blob storage — the read, the seed-if-missing guard
// that must never clobber a loaded AODC export, and the caching that keeps every details-page render
// from re-downloading a document that changes once a year.
[Collection(nameof(AzuriteCollection))]
public sealed class GradeReferenceBlobClientTests(AzuriteFixture azurite)
{
    private const string SeedJson = """
    {
      "60370683": {
        "qan": "60370683",
        "qualificationTitle": "Pearson BTEC L1/L2 Tech Award in Sport",
        "awardingOrganisation": "Pearson",
        "passGrades": ["*2", "P1", "P2", "M1", "M2", "D1", "D2"],
        "failGrades": ["F", "Q", "R", "U", "X"]
      }
    }
    """;

    private const string ReplacementJson = """
    {
      "99999999": {
        "qan": "99999999",
        "qualificationTitle": "Loaded from the real AODC export",
        "awardingOrganisation": "AODC",
        "passGrades": ["A"],
        "failGrades": ["U"]
      }
    }
    """;

    // Each test gets its own container so the shared Azurite instance stays test-independent.
    private (GradeReferenceBlobClient Client, BlobServiceClient Service, string Container) NewClient(
        IMemoryCache? cache = null)
    {
        var container = $"rules-config-{Guid.NewGuid():N}";
        var service = new BlobServiceClient(azurite.ConnectionString);
        var options = Options.Create(new BlobRulesProviderOptions { RulesBlobContainer = container });
        var client = new GradeReferenceBlobClient(
            service,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            options,
            NullLogger<GradeReferenceBlobClient>.Instance);
        return (client, service, container);
    }

    [Fact]
    public async Task Seeds_the_document_and_reads_it_back()
    {
        var (client, _, _) = NewClient();

        await client.SeedIfMissingAsync(SeedJson);
        var reference = await client.GetByQanAsync("60370683");

        Assert.NotNull(reference);
        Assert.Equal("Pearson BTEC L1/L2 Tech Award in Sport", reference.QualificationTitle);
        Assert.Equal(
            ["*2", "P1", "P2", "M1", "M2", "D1", "D2", "F", "Q", "R", "U", "X"],
            reference.AllGrades.ToArray());
    }

    [Fact]
    public async Task Seeds_into_the_configured_container_under_the_expected_blob_name()
    {
        var (client, service, container) = NewClient();

        await client.SeedIfMissingAsync(SeedJson);

        var blob = service.GetBlobContainerClient(container).GetBlobClient("grade-reference.json");
        Assert.True(await blob.ExistsAsync());
    }

    [Fact]
    public async Task Seeding_never_overwrites_an_existing_document()
    {
        // Once an environment has the real AODC export loaded, redeploying an older bundled copy
        // must not undo it. This is the whole point of the If-None-Match guard.
        var (client, _, _) = NewClient();
        await client.SeedIfMissingAsync(ReplacementJson);

        await client.SeedIfMissingAsync(SeedJson);

        Assert.NotNull(await client.GetByQanAsync("99999999"));
        Assert.Null(await client.GetByQanAsync("60370683"));
    }

    [Fact]
    public async Task Seeding_twice_does_not_throw()
    {
        var (client, _, _) = NewClient();

        await client.SeedIfMissingAsync(SeedJson);
        await client.SeedIfMissingAsync(SeedJson);

        Assert.NotNull(await client.GetByQanAsync("60370683"));
    }

    [Fact]
    public async Task A_missing_document_reads_as_no_grades_rather_than_throwing()
    {
        // The details page must degrade to "we cannot list grades yet", not 500.
        var (client, _, _) = NewClient();

        Assert.Null(await client.GetByQanAsync("60370683"));
    }

    [Fact]
    public async Task A_missing_container_reads_as_no_grades_rather_than_throwing()
    {
        var (client, _, _) = NewClient();

        Assert.Null(await client.GetByQanAsync("anything"));
    }

    [Fact]
    public async Task An_unknown_qan_returns_null()
    {
        var (client, _, _) = NewClient();
        await client.SeedIfMissingAsync(SeedJson);

        Assert.Null(await client.GetByQanAsync("00000000"));
    }

    [Fact]
    public async Task The_document_is_downloaded_once_and_then_served_from_cache()
    {
        var (client, service, container) = NewClient();
        await client.SeedIfMissingAsync(SeedJson);
        Assert.NotNull(await client.GetByQanAsync("60370683"));

        // Overwriting out-of-band and still seeing the old content proves the second read never
        // went to storage.
        await service.GetBlobContainerClient(container).GetBlobClient("grade-reference.json")
            .UploadAsync(BinaryData.FromString(ReplacementJson), overwrite: true);

        Assert.NotNull(await client.GetByQanAsync("60370683"));
        Assert.Null(await client.GetByQanAsync("99999999"));
    }

    [Fact]
    public async Task Seeding_invalidates_a_cached_empty_lookup()
    {
        // Startup order matters: if anything reads the reference before the seeder runs, the empty
        // result must not stick around for five minutes and leave every picker blank.
        var (client, _, _) = NewClient();
        Assert.Null(await client.GetByQanAsync("60370683"));

        await client.SeedIfMissingAsync(SeedJson);

        Assert.NotNull(await client.GetByQanAsync("60370683"));
    }

    [Fact]
    public async Task Malformed_stored_json_throws_so_a_corrupt_reference_file_surfaces()
    {
        var (client, service, container) = NewClient();
        var containerClient = service.GetBlobContainerClient(container);
        await containerClient.CreateAsync();
        await containerClient.UploadBlobAsync("grade-reference.json", BinaryData.FromString("{not json"));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => client.GetByQanAsync("60370683"));
    }
}
