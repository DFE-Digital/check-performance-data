using System.Reflection;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaLibraryControllerTests
{
    private readonly IMediaAssetRepository _media = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaUploadService _uploads = Substitute.For<IMediaUploadService>();
    private readonly IMediaUrlBuilder _urls = Substitute.For<IMediaUrlBuilder>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly MediaLibraryController _sut;

    public MediaLibraryControllerTests()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(25);
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([], 0, 1, 25));
        _urls.Rendition(Arg.Any<MediaAssetDto>(), Arg.Any<MediaAssetVersionDto>(), Arg.Any<int>()).Returns(ci => $"/thumb/{ci.Arg<MediaAssetDto>().Id}");
        _sut = new MediaLibraryController(_media, _uploads, _urls, _settings)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static MediaAssetDto Asset(string fileName = "chart.png", string? alt = null, string type = "image/png", int? width = 640, int? height = 480, long size = 2048)
    {
        var id = Guid.NewGuid();
        var version = new MediaAssetVersionDto(id, 1, MediaBlobKeys.Original(id, 1), type, width, height, size, "sha", false, DateTime.UtcNow, "u");
        return new MediaAssetDto(id, fileName, "chart", alt, null, null, [], 1, version, DateTime.UtcNow, "u", DateTime.UtcNow, "u");
    }

    private static IFormFile File(string name, int length = 10)
    {
        var bytes = new byte[length];
        return new FormFile(new MemoryStream(bytes), 0, length, "files", name);
    }

    private void AcceptJson() => _sut.HttpContext.Request.Headers.Accept = "application/json";

    [Fact]
    public void Controller_IsGatedByTheMediaSection()
    {
        var gate = typeof(MediaLibraryController).GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.Media, gate!.SectionKey);
    }

    [Fact]
    public async Task Index_PassesTheSearchFiltersSortAndPage_AtTheCmsPageLength()
    {
        await _sut.Index(search: " chart ", missingAlt: true, unused: true, sort: "name", page: 2, CancellationToken.None);

        await _media.Received(1).ListAsync(
            Arg.Is<MediaAssetQuery>(q => q.Search == "chart" && q.MissingAltText && q.UnusedOnly
                && q.Sort == MediaAssetSort.Name && q.Page == 2 && q.PageSize == 25),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bogus")]
    [InlineData("newest")]
    public async Task Index_SortsNewestFirst_UnlessNameIsAsked(string? sort)
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, sort, 1, CancellationToken.None));

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.Sort == MediaAssetSort.Newest), Arg.Any<CancellationToken>());
        Assert.Equal("newest", Assert.IsType<MediaLibraryIndexViewModel>(view.Model).Sort);
    }

    [Fact]
    public async Task Index_FallsBackToTwentyAPage_WhenThePageLengthIsNotSet()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(0);

        await _sut.Index(null, false, false, null, 1, CancellationToken.None);

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.PageSize == 20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_ShowsEachImage_WithA320PixelThumbnail_AndItsDetails()
    {
        var chart = Asset("chart.png", alt: "Bar chart", size: 1536);
        var logo = Asset("logo.svg", type: "image/svg+xml", width: null, height: null);
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([chart, logo], 2, 1, 25));

        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, 1, CancellationToken.None));

        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(2, model.Items.Count);
        var first = model.Items[0];
        Assert.Equal($"/thumb/{chart.Id}", first.ThumbnailUrl);
        Assert.Equal($"/admin/media/{chart.Id}", first.DetailUrl);
        Assert.Equal("640 × 480 pixels", first.Dimensions);
        Assert.Equal("1.5 KB", first.Size);
        Assert.False(first.MissingAltText);
        Assert.Null(model.Items[1].Dimensions);
        Assert.True(model.Items[1].MissingAltText);
        _urls.Received(1).Rendition(chart, chart.Latest, 320);
    }

    [Fact]
    public async Task Detail_IsNotFound_ForAnUnknownImage()
    {
        Assert.IsType<NotFoundResult>(await _sut.Detail(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Detail_ShowsTheImage()
    {
        var asset = Asset();
        _media.GetAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);

        var view = Assert.IsType<ViewResult>(await _sut.Detail(asset.Id, CancellationToken.None));

        Assert.Equal(asset.Id, Assert.IsType<MediaLibraryItemViewModel>(view.Model).Id);
    }

    [Fact]
    public void Upload_IsAnAntiforgeryCheckedPost_WithExplicitBodyLimits()
    {
        var method = typeof(MediaLibraryController).GetMethod(nameof(MediaLibraryController.Upload))!;

        Assert.Equal("admin/media/upload", method.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var sizeLimit = method.GetCustomAttributesData().Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(MediaLibraryController.MaxUploadRequestBytes, sizeLimit.ConstructorArguments[0].Value);
        Assert.Equal(MediaLibraryController.MaxUploadRequestBytes, method.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        // Each file must still fit the pipeline's own limit; the request limit only adds room for
        // more files in one plain form post.
        Assert.True(MediaLibraryController.MaxUploadRequestBytes > MediaUploadLimits.MaxFileBytes);
    }

    [Fact]
    public async Task Upload_ReportsEachFile_CreatedDuplicateOrRejected()
    {
        var created = Asset("new.png");
        var existing = Asset("old.png");
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(created));
        _uploads.UploadAsync(Arg.Any<Stream>(), "copy.png", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Duplicate(existing));
        _uploads.UploadAsync(Arg.Any<Stream>(), "notes.txt", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Rejected(MediaRejections.NotAnImage));

        var view = Assert.IsType<ViewResult>(await _sut.Upload([File("new.png"), File("copy.png"), File("notes.txt")], CancellationToken.None));

        Assert.Equal("Index", view.ViewName);
        var results = Assert.IsType<MediaLibraryIndexViewModel>(view.Model).UploadResults;
        Assert.Collection(results,
            r => { Assert.Equal(MediaUploadOutcome.Created, r.Outcome); Assert.Equal($"/admin/media/{created.Id}", r.DetailUrl); },
            r => { Assert.Equal(MediaUploadOutcome.Duplicate, r.Outcome); Assert.Equal("old.png", r.ExistingFileName); Assert.Equal($"/admin/media/{existing.Id}", r.DetailUrl); },
            r => { Assert.Equal(MediaUploadOutcome.Rejected, r.Outcome); Assert.Equal(MediaRejections.NotAnImage, r.Rejection); Assert.Equal("notes.txt", r.FileName); });
    }

    [Fact]
    public async Task Upload_WithNoFile_AsksForOne()
    {
        var view = Assert.IsType<ViewResult>(await _sut.Upload([], CancellationToken.None));

        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(MediaLibraryController.NoFileError, model.UploadError);
        Assert.Equal(400, _sut.HttpContext.Response.StatusCode);
        await _uploads.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default);
    }

    [Fact]
    public async Task Upload_AnswersJson_WhenTheScriptAsksForIt()
    {
        AcceptJson();
        var created = Asset("new.png");
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(created));

        var json = Assert.IsType<JsonResult>(await _sut.Upload([File("new.png")], CancellationToken.None));

        var results = Assert.IsAssignableFrom<IReadOnlyList<MediaUploadItemViewModel>>(json.Value);
        Assert.Equal("created", Assert.Single(results).OutcomeName);
    }

    [Fact]
    public async Task Upload_AnswersJson400_WhenTheScriptSendsNoFile()
    {
        AcceptJson();

        var json = Assert.IsType<JsonResult>(await _sut.Upload([], CancellationToken.None));

        Assert.Equal(400, json.StatusCode);
    }
}
