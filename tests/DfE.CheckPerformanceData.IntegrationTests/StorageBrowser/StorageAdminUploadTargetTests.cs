using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.StorageBrowser;

// #568: an upload lands under the prefix and folder it was given, and an unsafe prefix or
// folder is refused and writes nothing. Runs against real blob semantics (Azurite) so the
// assertions are on what storage holds afterwards.
[Collection(nameof(AzuriteCollection))]
public sealed class StorageAdminUploadTargetTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private StorageAdminController Sut() =>
        new(new Dictionary<string, BlobServiceClient> { ["app"] = _blobs },
            Options.Create(new StorageBrowserOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static IFormFile File(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    [Fact]
    public async Task Upload_WritesUnderThePrefixAndFolder_InTheAddressedContainer()
    {
        var target = $"target-{Guid.NewGuid():N}";
        await _blobs.CreateBlobContainerAsync(target);

        var result = await Sut().Upload("app", target, [File("a.csv", "x,y")], "reports/", "2026");

        Assert.IsType<RedirectResult>(result);
        var blob = _blobs.GetBlobContainerClient(target).GetBlobClient("reports/2026/a.csv");
        Assert.True(await blob.ExistsAsync());
        Assert.Equal("text/csv", (await blob.GetPropertiesAsync()).Value.ContentType);
    }

    [Theory]
    [InlineData("../", null)]
    [InlineData(null, "..")]
    [InlineData("x/../../", null)]
    public async Task Upload_WithAnUnsafePrefixOrFolder_Is404_AndWritesNothing(string? prefix, string? folder)
    {
        var target = $"target-{Guid.NewGuid():N}";
        await _blobs.CreateBlobContainerAsync(target);

        var result = await Sut().Upload("app", target, [File("a.csv", "x,y")], prefix, folder);

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(await _blobs.GetBlobContainerClient(target).GetBlobsAsync().ToListAsync());
    }
}
