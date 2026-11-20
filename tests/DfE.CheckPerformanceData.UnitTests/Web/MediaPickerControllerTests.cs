using System.Reflection;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaPickerControllerTests
{
    private readonly IMediaAssetRepository _media = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaUploadService _uploads = Substitute.For<IMediaUploadService>();
    private readonly IMediaUrlBuilder _urls = Substitute.For<IMediaUrlBuilder>();
    private readonly MediaPickerController _sut;

    public MediaPickerControllerTests()
    {
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([], 0, 1, 12));
        _urls.Rendition(Arg.Any<MediaAssetDto>(), Arg.Any<MediaAssetVersionDto>(), Arg.Any<int>())
            .Returns(ci => $"/r/{ci.Arg<MediaAssetDto>().Id}/{ci.ArgAt<int>(2)}");
        var http = new DefaultHttpContext();
        _sut = new MediaPickerController(_media, _uploads, _urls)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static MediaAssetDto Asset(string fileName = "chart.png", string? alt = "A bar chart", string type = "image/png",
        int? width = 640, int? height = 480, bool animated = false)
    {
        var id = Guid.NewGuid();
        var version = new MediaAssetVersionDto(id, 1, MediaBlobKeys.Original(id, 1), type, width, height, 2048, "sha", animated, DateTime.UtcNow, "u");
        return new MediaAssetDto(id, fileName, "chart", alt, null, null, [], 1, version, DateTime.UtcNow, "u", DateTime.UtcNow, "u");
    }

    private static IFormFile File(string name, int length = 10) =>
        new FormFile(new MemoryStream(new byte[length]), 0, length, "file", name);

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // Browsing and uploading from a page is for anyone who edits pages, not only library managers.
    [Fact]
    public void Controller_IsGatedByTheContentPagesSection_NotTheMediaSection()
    {
        var gate = typeof(MediaPickerController).GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.ContentPages, gate!.SectionKey);
    }

    [Fact]
    public void Upload_NeedsTheAntiforgeryToken_AndSetsItsOwnSizeLimits()
    {
        var action = typeof(MediaPickerController).GetMethod(nameof(MediaPickerController.Upload))!;

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var sizeLimit = action.GetCustomAttributesData().Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(MediaPickerController.MaxUploadRequestBytes, sizeLimit.ConstructorArguments[0].Value);
        Assert.Equal(MediaPickerController.MaxUploadRequestBytes,
            action.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        Assert.True(MediaPickerController.MaxUploadRequestBytes > MediaUploadLimits.MaxFileBytes);
    }

    [Fact]
    public async Task List_SearchesTheLibrary_OnePickerPageAtATime_NewestFirst()
    {
        await _sut.List(" chart ", 3, CancellationToken.None);

        await _media.Received(1).ListAsync(
            Arg.Is<MediaAssetQuery>(q => q.Search == "chart" && q.Page == 3 && q.PageSize == MediaPickerController.PageSize
                && q.Sort == MediaAssetSort.Newest && !q.UnusedOnly && !q.MissingAltText),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_TreatsABlankSearchAsNone_AndAPageBelowOneAsTheFirst()
    {
        await _sut.List("  ", 0, CancellationToken.None);

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.Search == null && q.Page == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_AnswersEachImage_WithItsThumbnailCropSurfaceAndFacts()
    {
        var png = Asset();
        var svg = Asset("logo.svg", null, "image/svg+xml", 64, 32);
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([png, svg], 30, 2, 12));

        var json = Json(await _sut.List(null, 2, CancellationToken.None));

        Assert.Equal(2, json.GetProperty("page").GetInt32());
        Assert.Equal(3, json.GetProperty("totalPages").GetInt32());
        Assert.Equal(30, json.GetProperty("totalCount").GetInt32());
        var items = json.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());

        var first = items[0];
        Assert.Equal(png.Id, first.GetProperty("id").GetGuid());
        Assert.Equal("chart.png", first.GetProperty("fileName").GetString());
        Assert.Equal("A bar chart", first.GetProperty("alt").GetString());
        Assert.Equal(640, first.GetProperty("width").GetInt32());
        Assert.Equal(480, first.GetProperty("height").GetInt32());
        Assert.Equal($"/r/{png.Id}/320", first.GetProperty("thumbUrl").GetString());
        Assert.Equal($"/r/{png.Id}/{MediaPickerItem.CropSurfaceWidth}", first.GetProperty("editUrl").GetString());
        Assert.False(first.GetProperty("isSvg").GetBoolean());
        Assert.False(first.GetProperty("isAnimated").GetBoolean());
        Assert.True(first.GetProperty("canCrop").GetBoolean());
        Assert.False(first.GetProperty("duplicate").GetBoolean());

        var second = items[1];
        Assert.True(second.GetProperty("isSvg").GetBoolean());
        Assert.False(second.GetProperty("canCrop").GetBoolean());
        Assert.Equal("", second.GetProperty("alt").GetString());
    }

    [Theory]
    [InlineData("image/gif", true)]
    [InlineData("image/webp", true)]
    [InlineData("image/gif", false)]
    public void AnAnimationOrAGif_CannotBeCropped(string type, bool animated)
    {
        var item = MediaPickerItem.From(Asset("a.gif", null, type, 10, 10, animated), _urls);

        Assert.Equal(animated, item.IsAnimated);
        Assert.False(item.CanCrop && (animated || type == "image/gif"));
    }

    [Fact]
    public async Task Upload_WithNoFile_AnswersAnError()
    {
        var result = await _sut.Upload(null, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, json.StatusCode);
        Assert.Equal(MediaPickerController.NoFileError, Json(result).GetProperty("error").GetString());
        await _uploads.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default);
    }

    [Fact]
    public async Task Upload_StoresTheFileThroughThePipeline_AndAnswersTheNewImage()
    {
        var asset = Asset("new.png", null);
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(asset));

        var result = await _sut.Upload(File("new.png"), CancellationToken.None);

        Assert.Null(Assert.IsType<JsonResult>(result).StatusCode);
        var json = Json(result);
        Assert.Equal(asset.Id, json.GetProperty("id").GetGuid());
        Assert.Equal("new.png", json.GetProperty("fileName").GetString());
        Assert.False(json.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Upload_OfAFileAlreadyInTheLibrary_AnswersTheExistingImage_MarkedDuplicate()
    {
        var existing = Asset("first.png");
        _uploads.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Duplicate(existing));

        var json = Json(await _sut.Upload(File("again.png"), CancellationToken.None));

        Assert.Equal(existing.Id, json.GetProperty("id").GetGuid());
        Assert.Equal("first.png", json.GetProperty("fileName").GetString());
        Assert.True(json.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Upload_OfARefusedFile_AnswersTheReason()
    {
        _uploads.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Rejected("The file must be an image"));

        var result = await _sut.Upload(File("notes.txt"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<JsonResult>(result).StatusCode);
        Assert.Equal("The file must be an image", Json(result).GetProperty("error").GetString());
    }

    // The preview is the widget's own view, so it matches the page by construction.
    [Fact]
    public void Preview_RendersTheImageWidgetView_InTheEditorContext_FromTheQuery()
    {
        var id = Guid.NewGuid();
        _sut.HttpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["mediaId"] = id.ToString(),
            ["alt"] = "Pupils",
            ["width"] = "one-half",
            ["crop"] = "0.25,0,0.5,1",
            ["aspect"] = "1:1",
            ["focal"] = "0.5,0.5",
            ["unknown"] = "dropped",
        });

        var result = Assert.IsType<PartialViewResult>(_sut.Preview());

        Assert.Equal(MediaPickerController.ImageWidgetView, result.ViewName);
        var widget = Assert.IsType<WidgetNode>(result.Model);
        Assert.Equal("image", widget.Type);
        Assert.Equal(id.ToString(), (string?)widget.Props!["mediaId"]);
        Assert.Equal("one-half", (string?)widget.Props["width"]);
        Assert.Equal("0.25,0,0.5,1", (string?)widget.Props["crop"]);
        Assert.Equal("", (string?)widget.Props["caption"]);
        Assert.False(widget.Props.ContainsKey("unknown"));
        var context = Assert.IsType<MediaRenderContext>(result.ViewData[MediaRenderContext.ViewDataKey]);
        Assert.True(context.ShowEditorNotices);
        Assert.Null(context.PageVersionId);
    }

    [Fact]
    public void Routes_AreUnderTheMediaPickerPath()
    {
        Assert.Equal("admin/media/picker", Route(nameof(MediaPickerController.List)));
        Assert.Equal("admin/media/picker/upload", Route(nameof(MediaPickerController.Upload)));
        Assert.Equal("admin/media/picker/preview", Route(nameof(MediaPickerController.Preview)));
    }

    private static string? Route(string action) =>
        typeof(MediaPickerController).GetMethod(action)!.GetCustomAttributes()
            .OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single().Template;
}
