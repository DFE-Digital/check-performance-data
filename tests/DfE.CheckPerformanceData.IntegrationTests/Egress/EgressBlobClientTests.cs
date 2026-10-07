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
        configured ? new Dictionary<string, BlobServiceClient> { [EgressBlobClient.ClientKey] = _blobs } : new Dictionary<string, BlobServiceClient>(),
        Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public async Task Uploads_under_the_prefix_with_csv_content_type_and_hash_metadata()
    {
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var runId = Guid.NewGuid();

        await Sut().UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n1,2"), "ABC", runId, CancellationToken.None);

        var props = await _blobs.GetBlobContainerClient("cypmd").GetBlobClient($"extracts_input/{name}").GetPropertiesAsync();
        Assert.Equal("text/csv", props.Value.ContentType);
        Assert.Equal("ABC", props.Value.Metadata["sha256"]);
        Assert.Equal(runId.ToString(), props.Value.Metadata["egressRunId"]);
    }

    // A same-named file (another run of the same key stage on the same day) is replaced, not refused.
    [Fact]
    public async Task Uploading_a_same_named_file_replaces_it_and_its_run_stamp()
    {
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var firstRun = Guid.NewGuid();
        var secondRun = Guid.NewGuid();
        var sut = Sut();
        await sut.UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n1,2"), "ABC", firstRun, CancellationToken.None);

        await sut.UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n3,4"), "DEF", secondRun, CancellationToken.None);

        var blob = _blobs.GetBlobContainerClient("cypmd").GetBlobClient($"extracts_input/{name}");
        Assert.Equal("A,B\r\n3,4", (await blob.DownloadContentAsync()).Value.Content.ToString());
        Assert.Equal("DEF", (await blob.GetPropertiesAsync()).Value.Metadata["sha256"]);
        Assert.False(await sut.DeleteIfOwnedByRunAsync(name, firstRun, CancellationToken.None));
        Assert.True(await sut.DeleteIfOwnedByRunAsync(name, secondRun, CancellationToken.None));
    }

    [Fact]
    public void Reports_configuration_and_the_target_description()
    {
        Assert.True(Sut().IsConfigured);
        Assert.False(Sut(configured: false).IsConfigured);
        Assert.Equal("cypmd/extracts_input", Sut().TargetDescription);
    }

    // S3/M1: the metadata-checked delete used by transfer compensation and the Abandon sweep.
    [Fact]
    public async Task DeleteIfOwnedByRun_deletes_a_blob_stamped_with_that_run_and_returns_true()
    {
        var runId = Guid.NewGuid();
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var sut = Sut();
        await sut.UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n1,2"), "ABC", runId, CancellationToken.None);

        var removed = await sut.DeleteIfOwnedByRunAsync(name, runId, CancellationToken.None);

        Assert.True(removed);
        Assert.False(await _blobs.GetBlobContainerClient("cypmd").GetBlobClient($"extracts_input/{name}").ExistsAsync());
    }

    [Fact]
    public async Task DeleteIfOwnedByRun_never_touches_a_blob_stamped_with_a_different_run()
    {
        var owningRun = Guid.NewGuid();
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var sut = Sut();
        await sut.UploadAsync(name, Encoding.UTF8.GetBytes("A,B\r\n1,2"), "ABC", owningRun, CancellationToken.None);

        var removed = await sut.DeleteIfOwnedByRunAsync(name, Guid.NewGuid(), CancellationToken.None);

        Assert.False(removed);
        Assert.True(await _blobs.GetBlobContainerClient("cypmd").GetBlobClient($"extracts_input/{name}").ExistsAsync());
    }

    [Fact]
    public async Task DeleteIfOwnedByRun_returns_false_for_a_blob_that_does_not_exist()
    {
        var removed = await Sut().DeleteIfOwnedByRunAsync($"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv", Guid.NewGuid(), CancellationToken.None);
        Assert.False(removed);
    }

    [Fact]
    public async Task DeleteIfOwnedByRun_never_touches_a_blob_this_service_never_stamped()
    {
        var name = $"CYPMD_LDS_KS4_RemoveLearners_{Guid.NewGuid():N}.csv";
        var container = _blobs.GetBlobContainerClient("cypmd");
        await container.CreateIfNotExistsAsync();
        await container.GetBlobClient($"extracts_input/{name}").UploadAsync(new BinaryData("A,B\r\n1,2"));

        Assert.False(await Sut().DeleteIfOwnedByRunAsync(name, Guid.NewGuid(), CancellationToken.None));
        Assert.True(await container.GetBlobClient($"extracts_input/{name}").ExistsAsync());
    }
}
