using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Journey;

// AB#304119: the "Not on roll" FE college list against real blob storage. The bundled file is the
// source of truth, so a seed replaces a blob that differs; a failed read keeps the list in use.
[Collection(nameof(AzuriteCollection))]
public sealed class NotOnRollCollegeListStoreTests(AzuriteFixture azurite)
{
    private const string FirstJson = """{ "colleges": [ { "laestab": "2118066" } ] }""";
    private const string SecondJson = """{ "colleges": [ { "laestab": "3138001" } ] }""";

    // Each test gets its own container so the shared Azurite instance stays test-independent.
    private (NotOnRollCollegeListStore Store, BlobContainerClient Container) NewStore()
    {
        var name = $"rules-config-{Guid.NewGuid():N}";
        var service = new BlobServiceClient(azurite.ConnectionString);
        var store = new NotOnRollCollegeListStore(
            service,
            Options.Create(new BlobRulesProviderOptions { RulesBlobContainer = name }),
            NullLogger<NotOnRollCollegeListStore>.Instance);
        return (store, service.GetBlobContainerClient(name));
    }

    private static async Task<string> BlobText(BlobContainerClient container) =>
        (await container.GetBlobClient(NotOnRollCollegeList.BlobName).DownloadContentAsync())
            .Value.Content.ToString();

    [Fact]
    public async Task Seeds_a_missing_blob_and_reads_it_back()
    {
        var (store, container) = NewStore();

        await store.SeedAsync(FirstJson);
        await store.RefreshAsync();

        Assert.Equal(FirstJson, await BlobText(container));
        Assert.True(store.Current.Contains("211/8066"));
    }

    [Fact]
    public async Task A_seed_replaces_a_blob_that_differs_from_the_bundled_copy()
    {
        // A change to the list goes live by deploying it: the next startup overwrites the blob.
        var (store, container) = NewStore();
        await store.SeedAsync(FirstJson);

        await store.SeedAsync(SecondJson);
        await store.RefreshAsync();

        Assert.Equal(SecondJson, await BlobText(container));
        Assert.False(store.Current.Contains("2118066"));
        Assert.True(store.Current.Contains("3138001"));
    }

    [Fact]
    public async Task A_seed_does_not_rewrite_a_blob_that_already_matches()
    {
        var (store, container) = NewStore();
        await store.SeedAsync(FirstJson);
        var blob = container.GetBlobClient(NotOnRollCollegeList.BlobName);
        var before = (await blob.GetPropertiesAsync()).Value.ETag;

        await store.SeedAsync(FirstJson);

        Assert.Equal(before, (await blob.GetPropertiesAsync()).Value.ETag);
    }

    [Fact]
    public async Task A_missing_blob_keeps_the_bundled_list()
    {
        // Storage has not been seeded (or the seed failed): a college must still see the reason.
        var (store, _) = NewStore();
        store.UseBundled(FirstJson);

        await store.RefreshAsync();

        Assert.True(store.Current.Contains("2118066"));
    }

    [Fact]
    public async Task A_malformed_blob_keeps_the_list_already_loaded()
    {
        var (store, container) = NewStore();
        await store.SeedAsync(FirstJson);
        await store.RefreshAsync();
        await container.GetBlobClient(NotOnRollCollegeList.BlobName)
            .UploadAsync(BinaryData.FromString("{ not json"), overwrite: true);

        await store.RefreshAsync();

        Assert.True(store.Current.Contains("2118066"));
    }
}
