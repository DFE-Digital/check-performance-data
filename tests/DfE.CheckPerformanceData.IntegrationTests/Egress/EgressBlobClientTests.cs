using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Egress;

[Collection(nameof(AzuriteCollection))]
public sealed class EgressBlobClientTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);
    private static readonly EgressStorageOptions Options = new() { Container = "cypmd", Prefix = "extracts_input/" };

    private EgressBlobClient Sut(bool configured = true) => new(
        configured ? new Dictionary<string, BlobServiceClient> { ["egress"] = _blobs } : new Dictionary<string, BlobServiceClient>(),
        Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public async Task Uploads_under_the_prefix_with_csv_content_type_and_hash_metadata_and_never_overwrites()
    {
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var sut = Sut();

        await sut.UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n1,2"), "ABC", Guid.NewGuid(), CancellationToken.None);

        var blob = _blobs.GetBlobContainerClient("cypmd").GetBlobClient($"extracts_input/{name}");
        var props = await blob.GetPropertiesAsync();
        Assert.Equal("text/csv", props.Value.ContentType);
        Assert.Equal("ABC", props.Value.Metadata["sha256"]);
        await Assert.ThrowsAsync<EgressBlobAlreadyExistsException>(() => sut.UploadAsync(name, [1], "X", Guid.NewGuid(), CancellationToken.None));

        await sut.DeleteIfExistsAsync(name, CancellationToken.None);
        Assert.False(await blob.ExistsAsync());
        await sut.DeleteIfExistsAsync(name, CancellationToken.None);   // idempotent
    }

    [Fact]
    public void Reports_configuration_and_the_target_description()
    {
        Assert.True(Sut().IsConfigured);
        Assert.False(Sut(configured: false).IsConfigured);
        Assert.Equal("cypmd/extracts_input", Sut().TargetDescription);
    }
}
