using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.ResultsEnquiry;

// AB#297848: the qualification reference against real blob storage — the read, the seed-if-missing
// guard that must never clobber a loaded QualList export, and the caching that keeps every
// qualification-search render from re-downloading a document that changes rarely.
[Collection(nameof(AzuriteCollection))]
public sealed class QualificationReferenceBlobClientTests(AzuriteFixture azurite)
{
    private const string SeedJson = """
    {
      "60146084": {
        "qan": "60146084",
        "qualificationTitle": "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
        "awardingOrganisation": "AQA",
        "grades": ["1","2","3","4","5","6","7","8","9","Q","R","U","X"],
        "syllabusCodes": [ { "code": "8300F", "title": "Mathematics Foundation Tier" },
                           { "code": "8300H", "title": "Mathematics Higher Tier" } ]
      }
    }
    """;

    private const string ReplacementJson = """
    {
      "99999999": {
        "qan": "99999999",
        "qualificationTitle": "Loaded from the real QualList export",
        "awardingOrganisation": "QualList",
        "grades": ["A"],
        "syllabusCodes": []
      }
    }
    """;

    // Each test gets its own container so the shared Azurite instance stays test-independent.
    private (QualificationReferenceBlobClient Client, BlobServiceClient Service, string Container) NewClient(
        IMemoryCache? cache = null)
    {
        var container = $"rules-config-{Guid.NewGuid():N}";
        var service = new BlobServiceClient(azurite.ConnectionString);
        var options = Options.Create(new BlobRulesProviderOptions { RulesBlobContainer = container });
        var client = new QualificationReferenceBlobClient(
            service,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            options,
            NullLogger<QualificationReferenceBlobClient>.Instance);
        return (client, service, container);
    }

    [Fact]
    public async Task Seeds_only_when_absent_and_reads_back_the_lookup()
    {
        // Seed once, read: the E2E QAN resolves. Seed again with different content: the first
        // write must win (If-None-Match=*), because a hand-edited deployed blob must never be
        // clobbered by a pod restart.
        var (client, _, _) = NewClient();

        await client.SeedIfMissingAsync(SeedJson);
        var qualification = (await client.GetLookupAsync()).Find("60146084");
        Assert.NotNull(qualification);
        Assert.Equal("AQA", qualification.AwardingOrganisation);

        await client.SeedIfMissingAsync(ReplacementJson);

        Assert.NotNull((await client.GetLookupAsync()).Find("60146084"));
        Assert.Null((await client.GetLookupAsync()).Find("99999999"));
    }

    [Fact]
    public async Task A_missing_blob_reads_as_the_empty_lookup_not_an_exception()
    {
        // The qualification page degrades to empty dropdowns rather than a 500.
        var (client, _, _) = NewClient();

        var lookup = await client.GetLookupAsync();

        Assert.Same(QualificationReferenceLookup.Empty, lookup);
    }

    [Fact]
    public async Task Seeds_into_the_configured_container_under_the_expected_blob_name()
    {
        var (client, service, container) = NewClient();

        await client.SeedIfMissingAsync(SeedJson);

        var blob = service.GetBlobContainerClient(container).GetBlobClient("qualification-reference.json");
        Assert.True(await blob.ExistsAsync());
    }

    [Fact]
    public async Task The_document_is_downloaded_once_and_then_served_from_cache()
    {
        var (client, service, container) = NewClient();
        await client.SeedIfMissingAsync(SeedJson);
        Assert.NotNull((await client.GetLookupAsync()).Find("60146084"));

        // Overwriting out-of-band and still seeing the old content proves the second read never
        // went to storage.
        await service.GetBlobContainerClient(container).GetBlobClient("qualification-reference.json")
            .UploadAsync(BinaryData.FromString(ReplacementJson), overwrite: true);

        Assert.NotNull((await client.GetLookupAsync()).Find("60146084"));
        Assert.Null((await client.GetLookupAsync()).Find("99999999"));
    }

    [Fact]
    public async Task Seeding_invalidates_a_cached_empty_lookup()
    {
        // Startup order matters: if anything reads the reference before the seeder runs, the empty
        // result must not stick around for five minutes and leave every dropdown blank.
        var (client, _, _) = NewClient();
        Assert.Same(QualificationReferenceLookup.Empty, await client.GetLookupAsync());

        await client.SeedIfMissingAsync(SeedJson);

        Assert.NotNull((await client.GetLookupAsync()).Find("60146084"));
    }

    [Fact]
    public async Task Malformed_stored_json_throws_so_a_corrupt_reference_file_surfaces()
    {
        var (client, service, container) = NewClient();
        var containerClient = service.GetBlobContainerClient(container);
        await containerClient.CreateAsync();
        await containerClient.UploadBlobAsync("qualification-reference.json", BinaryData.FromString("{not json"));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => client.GetLookupAsync());
    }
}
