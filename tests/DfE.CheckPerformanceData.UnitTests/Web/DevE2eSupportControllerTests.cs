using System.Reflection;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The sample-image seed for the E2E suite exists only where dev tools are on and the
// environment is not production, and it is a form post that needs the antiforgery token.
public sealed class DevE2eSupportControllerTests
{
    private readonly IMediaUploadService _uploads = Substitute.For<IMediaUploadService>();
    private readonly IMediaUrlBuilder _urls = Substitute.For<IMediaUrlBuilder>();
    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();

    private static DevE2eSupportController Controller(bool devTools, string environment = "Development")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = devTools ? "true" : "false" })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        return new DevE2eSupportController(configuration, env);
    }

    private static MediaAssetDto Asset()
    {
        var id = Guid.NewGuid();
        var version = new MediaAssetVersionDto(id, 1, MediaBlobKeys.Original(id, 1), "image/png", 64, 32, 10, "sha", false, DateTime.UtcNow, "u");
        return new MediaAssetDto(id, "sample.png", "sample", null, null, null, [], 1, version, DateTime.UtcNow, "u", DateTime.UtcNow, "u");
    }

    [Theory]
    [InlineData(false, "Development")]
    [InlineData(true, "Production")]
    public async Task MediaSample_IsNotFound_OutsideDevTools(bool devTools, string environment)
    {
        var result = await Controller(devTools, environment).MediaSample("png", null, _uploads, _urls, _assets, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _uploads.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default);
    }

    [Fact]
    public void MediaSample_IsAPostThatNeedsTheAntiforgeryToken()
    {
        var method = typeof(DevE2eSupportController).GetMethod(nameof(DevE2eSupportController.MediaSample))!;

        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task MediaSample_UploadsTheSampleAndReturnsItsUrl()
    {
        var asset = Asset();
        _uploads.UploadAsync(Arg.Any<Stream>(), "sample.svg", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(asset));
        _urls.Original(asset, asset.Latest).Returns("/media/x/1/sample.svg");

        var result = await Controller(devTools: true).MediaSample("svg", null, _uploads, _urls, _assets, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Contains("/media/x/1/sample.svg", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task MediaSample_RefusesAnUnknownKind()
    {
        var result = await Controller(devTools: true).MediaSample("exe", null, _uploads, _urls, _assets, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    // The library list's "Missing alt text" filter needs an image that has alt text, and no page
    // sets alt text yet, so the seed can give the sample some.
    [Fact]
    public async Task MediaSample_SetsTheAltText_WhenOneIsGiven()
    {
        var asset = Asset();
        _uploads.UploadAsync(Arg.Any<Stream>(), "sample.svg", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Duplicate(asset));
        _assets.UpdateMetadataAsync(asset.Id, Arg.Any<MediaAssetMetadata>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Controller(devTools: true).MediaSample("svg", "Green and blue blocks", _uploads, _urls, _assets, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await _assets.Received(1).UpdateMetadataAsync(
            asset.Id, Arg.Is<MediaAssetMetadata>(m => m.DefaultAltText == "Green and blue blocks"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MediaSample_LeavesTheMetadataAlone_WithoutAltText()
    {
        var asset = Asset();
        _uploads.UploadAsync(Arg.Any<Stream>(), "sample.png", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(asset));

        await Controller(devTools: true).MediaSample("png", null, _uploads, _urls, _assets, CancellationToken.None);

        await _assets.DidNotReceiveWithAnyArgs().UpdateMetadataAsync(default, default!, default);
    }
}
