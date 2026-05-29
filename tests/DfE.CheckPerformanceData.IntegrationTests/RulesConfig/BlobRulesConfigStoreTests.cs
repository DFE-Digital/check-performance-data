using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesConfig;

[Collection(nameof(AzuriteCollection))]
public sealed class BlobRulesConfigStoreTests(AzuriteFixture fixture)
{
    private readonly AzuriteFixture _fixture = fixture;

    private BlobRulesConfigStore CreateStore()
    {
        var options = new BlobRulesProviderOptions { RulesBlobContainer = $"rules-{Guid.NewGuid():N}" };
        return new BlobRulesConfigStore(new BlobServiceClient(_fixture.ConnectionString), Options.Create(options));
    }

    [Fact]
    public async Task Write_then_read_round_trips_content_and_returns_etag()
    {
        var store = CreateStore();
        await store.WriteAsync(RulesConfigType.Rules, "{\"version\":\"t1\"}", expectedETag: null);
        var read = await store.ReadAsync(RulesConfigType.Rules);
        Assert.Equal("{\"version\":\"t1\"}", read.Content);
        Assert.False(string.IsNullOrEmpty(read.ETag));
    }

    [Fact]
    public async Task Write_with_stale_etag_throws_conflict()
    {
        var store = CreateStore();
        await store.WriteAsync(RulesConfigType.Lookups, "{}", expectedETag: null);
        var first = await store.ReadAsync(RulesConfigType.Lookups);
        await store.WriteAsync(RulesConfigType.Lookups, "{\"x\":1}", first.ETag);
        await Assert.ThrowsAsync<RulesConfigConflictException>(() =>
            store.WriteAsync(RulesConfigType.Lookups, "{\"y\":2}", first.ETag));
    }
}
