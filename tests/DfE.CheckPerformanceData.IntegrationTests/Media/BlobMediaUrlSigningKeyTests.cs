using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The media URL signing key is made once and kept in blob storage, encrypted with the app's Data
// Protection keys. Every instance of the service must read the same key, or a URL made by one is
// refused by another.
public sealed class BlobMediaUrlSigningKeyTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly string _container = $"keys-{Guid.NewGuid():N}";
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    private BlobMediaUrlSigningKey Key(IDataProtectionProvider? protection = null) =>
        new(_blobs, protection ?? _protection, NullLogger<BlobMediaUrlSigningKey>.Instance, _container);

    [Fact]
    public void TheFirstRead_MakesARandom256BitKey()
    {
        var key = Key().GetKey();

        Assert.Equal(32, key.Length);
        Assert.NotEqual(new byte[32], key);
    }

    [Fact]
    public void EveryInstance_ReadsTheSameKey()
    {
        var first = Key().GetKey();

        Assert.Equal(first, Key().GetKey());
        Assert.Equal(first, Key().GetKey());
    }

    [Fact]
    public async Task InstancesStartingTogether_AgreeOnOneKey()
    {
        var keys = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Key().GetKey())));

        Assert.All(keys, k => Assert.Equal(keys[0], k));
    }

    [Fact]
    public void TheStoredBlob_IsEncrypted()
    {
        var key = Key().GetKey();

        var stored = _blobs.GetBlobContainerClient(_container).GetBlobClient(BlobMediaUrlSigningKey.BlobName).DownloadContent().Value.Content.ToArray();
        Assert.False(stored.AsSpan().IndexOf(key) >= 0);
        Assert.NotEqual(Convert.ToBase64String(key), System.Text.Encoding.UTF8.GetString(stored));
    }

    // When the Data Protection keys are lost the stored key cannot be read any more. A new key is
    // made; URLs are built when pages render, so pages pick it up at once.
    [Fact]
    public void AKeyThatCannotBeDecrypted_IsReplaced()
    {
        var old = Key().GetKey();

        var replaced = Key(new EphemeralDataProtectionProvider()).GetKey();

        Assert.NotEqual(old, replaced);
        Assert.Equal(32, replaced.Length);
    }
}
