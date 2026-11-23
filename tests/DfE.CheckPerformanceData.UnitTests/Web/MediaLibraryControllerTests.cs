using System.Reflection;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaLibraryControllerTests
{
    private readonly IMediaAssetRepository _media = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaUploadService _uploads = Substitute.For<IMediaUploadService>();
    private readonly IMediaUrlBuilder _urls = Substitute.For<IMediaUrlBuilder>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly IMediaDeletionService _deletions = Substitute.For<IMediaDeletionService>();
    private readonly IMediaFolderService _folders = Substitute.For<IMediaFolderService>();
    private readonly MediaLibraryController _sut;

    private static readonly Guid Photos = Guid.NewGuid();
    private static readonly Guid Schools = Guid.NewGuid();
    private static readonly MediaFolderTree Tree = new([new MediaFolderDto(Photos, "Photos", null), new MediaFolderDto(Schools, "Schools", Photos)]);

    public MediaLibraryControllerTests()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(25);
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([], 0, 1, 25));
        _urls.Rendition(Arg.Any<MediaAssetDto>(), Arg.Any<MediaAssetVersionDto>(), Arg.Any<int>()).Returns(ci => $"/thumb/{ci.Arg<MediaAssetDto>().Id}");
        _urls.Original(Arg.Any<MediaAssetDto>(), Arg.Any<MediaAssetVersionDto>()).Returns(ci => $"/media/{ci.Arg<MediaAssetDto>().Id}/{ci.Arg<MediaAssetVersionDto>().Version}/chart.png");
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("cms.example");
        _folders.GetTreeAsync(Arg.Any<CancellationToken>()).Returns(Tree);
        _sut = new MediaLibraryController(_media, _uploads, _urls, _settings, _deletions, _folders)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>())
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
        await _sut.Index(search: " chart ", missingAlt: true, unused: true, sort: "name", view: null, page: 2, ct: CancellationToken.None);

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
        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, sort, null, 1, ct: CancellationToken.None));

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.Sort == MediaAssetSort.Newest), Arg.Any<CancellationToken>());
        Assert.Equal("newest", Assert.IsType<MediaLibraryIndexViewModel>(view.Model).Sort);
    }

    [Fact]
    public async Task Index_FallsBackToTwentyAPage_WhenThePageLengthIsNotSet()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(0);

        await _sut.Index(null, false, false, null, null, 1, ct: CancellationToken.None);

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.PageSize == 20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_ShowsEachImage_WithA320PixelThumbnail_AndItsDetails()
    {
        var chart = Asset("chart.png", alt: "Bar chart", size: 1536);
        var logo = Asset("logo.svg", type: "image/svg+xml", width: null, height: null);
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([chart, logo], 2, 1, 25));

        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, null, 1, ct: CancellationToken.None));

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

    [Theory]
    [InlineData(null, "grid")]
    [InlineData("bogus", "grid")]
    [InlineData("details", "details")]
    [InlineData("large", "large")]
    public async Task Index_ShowsTheViewAsked_AndTheGridForAnythingElse(string? asked, string expected)
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, asked, 1, ct: CancellationToken.None));

        Assert.Equal(expected, Assert.IsType<MediaLibraryIndexViewModel>(view.Model).View);
    }

    [Fact]
    public async Task Index_GivesEachImage_ItsUses_WhoUploadedItsFile_AndALargeCopy()
    {
        var chart = Asset("chart.png");
        var logo = Asset("logo.png");
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([chart, logo], 2, 1, 25));
        _media.CountUsesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [chart.Id] = 3, [logo.Id] = 0 });
        _urls.Rendition(Arg.Any<MediaAssetDto>(), Arg.Any<MediaAssetVersionDto>(), 960).Returns(ci => $"/large/{ci.Arg<MediaAssetDto>().Id}");

        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, "details", 1, ct: CancellationToken.None));

        var items = Assert.IsType<MediaLibraryIndexViewModel>(view.Model).Items;
        Assert.Equal(3, items[0].Uses);
        Assert.Equal(0, items[1].Uses);
        Assert.Equal("u", items[0].UploadedBy);
        Assert.Equal(chart.Latest.UploadedAtUtc, items[0].UploadedAtUtc);
        Assert.Equal($"/large/{chart.Id}", items[0].LargeUrl);
        await _media.Received(1).CountUsesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { chart.Id, logo.Id })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_IsNotFound_ForAnUnknownImage()
    {
        Assert.IsType<NotFoundResult>(await _sut.Detail(Guid.NewGuid(), ct: CancellationToken.None));
    }

    [Fact]
    public async Task Detail_ShowsThePreview_TheFacts_ThePublicLink_TheForm_TheVersionsAndTheUsages()
    {
        var asset = Asset(alt: "Bar chart") with { Caption = "Results", Credit = "DfE", Tags = ["schools", "KS2"], CurrentVersion = 2 };
        var v2 = asset.Latest with { Version = 2, UploadedBy = "editor@education.gov.uk", Width = 800, Height = 600 };
        var v1 = asset.Latest with { Version = 1, UploadedBy = "first@education.gov.uk" };
        asset = asset with { Latest = v2 };
        var page = new MediaUsagePage(Guid.NewGuid(), "Attendance", "attendance", false, true);
        _media.GetAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        _media.GetVersionsAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([v2, v1]);
        _media.GetUsagePagesAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([page]);
        var block = new MediaUsageBlock(7, "home-intro");
        _media.GetUsageBlocksAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([block]);
        _urls.Rendition(asset, v2, 960).Returns("/preview-960");

        var view = Assert.IsType<ViewResult>(await _sut.Detail(asset.Id, ct: CancellationToken.None));

        Assert.Equal("Detail", view.ViewName);
        var model = Assert.IsType<MediaDetailViewModel>(view.Model);
        Assert.Equal(asset.Id, model.Item.Id);
        Assert.Equal("/preview-960", model.PreviewUrl);
        Assert.Equal($"https://cms.example/media/{asset.Id}/2/chart.png", model.PublicUrl);
        Assert.Equal("editor@education.gov.uk", model.UploadedBy);
        Assert.Equal("Bar chart", model.Metadata.AltText);
        Assert.Equal("Results", model.Metadata.Caption);
        Assert.Equal("DfE", model.Metadata.Credit);
        Assert.Equal("schools, KS2", model.Metadata.Tags);
        Assert.Collection(model.Versions,
            v => { Assert.Equal(2, v.Version); Assert.True(v.IsCurrent); Assert.Equal("800 × 600 pixels", v.Dimensions); Assert.Equal("editor@education.gov.uk", v.UploadedBy); },
            v => { Assert.Equal(1, v.Version); Assert.False(v.IsCurrent); Assert.Equal("640 × 480 pixels", v.Dimensions); });
        // Each version's thumbnail is made from that version's own file.
        _urls.Received(1).Rendition(asset, v1, 320);
        Assert.Equal([page], model.UsagePages);
        Assert.Equal([block], model.Usage.Blocks);
        Assert.Equal($"/admin/media/{asset.Id}/delete", model.DeleteUrl);
    }

    private MediaAssetDto Known(MediaAssetDto? asset = null)
    {
        asset ??= Asset();
        _media.GetAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        _media.GetVersionsAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([asset.Latest]);
        _media.GetUsagePagesAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([]);
        _media.GetUsageBlocksAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([]);
        return asset;
    }

    [Theory]
    [InlineData(nameof(MediaLibraryController.UpdateDetails), "admin/media/{id:guid}/details")]
    [InlineData(nameof(MediaLibraryController.Replace), "admin/media/{id:guid}/replace")]
    [InlineData(nameof(MediaLibraryController.Delete), "admin/media/{id:guid}/delete")]
    [InlineData(nameof(MediaLibraryController.DeleteUnused), "admin/media/delete-unused")]
    [InlineData(nameof(MediaLibraryController.CreateFolder), "admin/media/folders")]
    [InlineData(nameof(MediaLibraryController.RenameFolder), "admin/media/folders/{id:guid}/rename")]
    [InlineData(nameof(MediaLibraryController.MoveFolder), "admin/media/folders/{id:guid}/move")]
    [InlineData(nameof(MediaLibraryController.DeleteFolder), "admin/media/folders/{id:guid}/delete")]
    [InlineData(nameof(MediaLibraryController.MoveImages), "admin/media/move")]
    public void EveryChange_IsAnAntiforgeryCheckedPost(string action, string template)
    {
        var method = typeof(MediaLibraryController).GetMethod(action)!;

        Assert.Equal(template, method.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Replace_HasExplicitBodyLimits_ForOneFile()
    {
        var method = typeof(MediaLibraryController).GetMethod(nameof(MediaLibraryController.Replace))!;

        var sizeLimit = method.GetCustomAttributesData().Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(MediaLibraryController.MaxReplaceRequestBytes, sizeLimit.ConstructorArguments[0].Value);
        Assert.Equal(MediaLibraryController.MaxReplaceRequestBytes, method.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        Assert.InRange(MediaLibraryController.MaxReplaceRequestBytes, MediaUploadLimits.MaxFileBytes + 1, MediaLibraryController.MaxUploadRequestBytes);
    }

    [Fact]
    public async Task UpdateDetails_SavesTheForm_AndGoesBackToTheImage_WithASuccessMessage()
    {
        var asset = Known();
        _media.UpdateMetadataAsync(asset.Id, Arg.Any<MediaAssetMetadata>(), Arg.Any<CancellationToken>()).Returns(true);
        var form = new MediaMetadataForm { AltText = " Bar chart ", Caption = "", Credit = "DfE", Tags = " schools, KS2 ,, " };

        var redirect = Assert.IsType<RedirectResult>(await _sut.UpdateDetails(asset.Id, form, ct: CancellationToken.None));

        Assert.Equal($"/admin/media/{asset.Id}", redirect.Url);
        await _media.Received(1).UpdateMetadataAsync(asset.Id,
            Arg.Is<MediaAssetMetadata>(m => m.DefaultAltText == " Bar chart " && m.Credit == "DfE" && m.Tags.SequenceEqual(new[] { "schools", "KS2" })),
            Arg.Any<CancellationToken>());
        Assert.Equal("Image details saved", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task UpdateDetails_WithErrors_ShowsThePageAgain_AndSavesNothing()
    {
        var asset = Known();
        _sut.ModelState.AddModelError("Metadata.AltText", "Alt text must be 1000 characters or fewer");

        var view = Assert.IsType<ViewResult>(await _sut.UpdateDetails(asset.Id, new MediaMetadataForm { AltText = "x" }, ct: CancellationToken.None));

        Assert.Equal("Detail", view.ViewName);
        Assert.Equal(asset.Id, Assert.IsType<MediaDetailViewModel>(view.Model).Item.Id);
        await _media.DidNotReceiveWithAnyArgs().UpdateMetadataAsync(default, default!, default);
    }

    [Fact]
    public async Task UpdateDetails_IsNotFound_ForAnUnknownImage()
    {
        var id = Guid.NewGuid();
        _media.UpdateMetadataAsync(id, Arg.Any<MediaAssetMetadata>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.IsType<NotFoundResult>(await _sut.UpdateDetails(id, new MediaMetadataForm(), ct: CancellationToken.None));
    }

    [Fact]
    public async Task Replace_StoresANewVersion_AndGoesBackToTheImage_WithASuccessMessage()
    {
        var asset = Known();
        var v2 = asset.Latest with { Version = 2 };
        _uploads.ReplaceAsync(asset.Id, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(MediaReplaceResult.Replaced(v2));

        var redirect = Assert.IsType<RedirectResult>(await _sut.Replace(asset.Id, File("new.png"), ct: CancellationToken.None));

        Assert.Equal($"/admin/media/{asset.Id}", redirect.Url);
        Assert.Equal("Version 2 uploaded", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    // A refused file and a replace that lost a race both put their reason on the file control.
    [Theory]
    [InlineData(MediaRejections.NotAnImage)]
    [InlineData(MediaRejections.ReplacedConcurrently)]
    public async Task Replace_Refused_ShowsTheReasonOnTheFileControl(string reason)
    {
        var asset = Known();
        _uploads.ReplaceAsync(asset.Id, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(MediaReplaceResult.Rejected(reason));

        var view = Assert.IsType<ViewResult>(await _sut.Replace(asset.Id, File("new.png"), ct: CancellationToken.None));

        Assert.Equal("Detail", view.ViewName);
        Assert.Equal(reason, Assert.Single(_sut.ModelState[MediaLibraryController.ReplaceFileField]!.Errors).ErrorMessage);
        Assert.Equal(400, _sut.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Replace_WithNoFile_AsksForOne()
    {
        var asset = Known();

        var view = Assert.IsType<ViewResult>(await _sut.Replace(asset.Id, null, ct: CancellationToken.None));

        Assert.Equal("Detail", view.ViewName);
        Assert.Equal(MediaLibraryController.NoReplaceFileError, Assert.Single(_sut.ModelState[MediaLibraryController.ReplaceFileField]!.Errors).ErrorMessage);
        await _uploads.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default!, default);
    }

    [Fact]
    public async Task Replace_IsNotFound_ForAnUnknownImage()
    {
        var id = Guid.NewGuid();
        _uploads.ReplaceAsync(id, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(MediaReplaceResult.NotFound());

        Assert.IsType<NotFoundResult>(await _sut.Replace(id, File("new.png"), ct: CancellationToken.None));
    }

    [Fact]
    public async Task DeleteConfirm_ForAnUnusedImage_OffersTheDelete()
    {
        var asset = Known();

        var view = Assert.IsType<ViewResult>(await _sut.DeleteConfirm(asset.Id, ct: CancellationToken.None));

        Assert.Equal("Delete", view.ViewName);
        var model = Assert.IsType<MediaDeleteViewModel>(view.Model);
        Assert.True(model.CanDelete);
        Assert.Equal(asset.FileName, model.FileName);
    }

    [Fact]
    public async Task DeleteConfirm_ForAUsedImage_ListsThePages_AndOffersNoDelete()
    {
        var asset = Known();
        _media.GetUsagePagesAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(
            [new MediaUsagePage(Guid.NewGuid(), "Attendance", "attendance", false, true), new MediaUsagePage(Guid.NewGuid(), "Old", "old", true, false)]);

        var model = Assert.IsType<MediaDeleteViewModel>(Assert.IsType<ViewResult>(await _sut.DeleteConfirm(asset.Id, ct: CancellationToken.None)).Model);

        Assert.False(model.CanDelete);
        Assert.Equal("1 page", model.Usage.UsedOn);
        Assert.Equal(["Attendance"], model.Usage.CurrentPages.Select(p => p.Title));
        Assert.Equal(["Old"], model.Usage.OlderPages.Select(p => p.Title));
    }

    [Fact]
    public async Task DeleteConfirm_ForAnImageUsedOnlyInAContentBlock_OffersNoDelete()
    {
        var asset = Known();
        _media.GetUsageBlocksAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([new MediaUsageBlock(3, "contact-highlight")]);

        var model = Assert.IsType<MediaDeleteViewModel>(Assert.IsType<ViewResult>(await _sut.DeleteConfirm(asset.Id, ct: CancellationToken.None)).Model);

        Assert.False(model.CanDelete);
        Assert.Equal("1 content block", model.Usage.UsedOn);
        Assert.Equal(["contact-highlight"], model.Usage.Blocks.Select(b => b.Key));
    }

    [Fact]
    public async Task DeleteConfirm_IsNotFound_ForAnUnknownImage()
    {
        Assert.IsType<NotFoundResult>(await _sut.DeleteConfirm(Guid.NewGuid(), ct: CancellationToken.None));
    }

    // The confirmation page lists the unused images that match the search, the most one run takes.
    [Fact]
    public async Task DeleteUnusedConfirm_ListsTheUnusedImagesThatMatchTheSearch()
    {
        var a = Asset("a.png");
        var b = Asset("b.png");
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([a, b], 2, 1, MediaDeletionService.MaxUnusedPerRun));

        var view = Assert.IsType<ViewResult>(await _sut.DeleteUnusedConfirm("charts", ct: CancellationToken.None));

        await _media.Received(1).ListAsync(
            Arg.Is<MediaAssetQuery>(q => q.UnusedOnly && q.Search == "charts" && q.Page == 1
                && q.PageSize == MediaDeletionService.MaxUnusedPerRun && q.Sort == MediaAssetSort.Name),
            Arg.Any<CancellationToken>());
        Assert.Equal("DeleteUnused", view.ViewName);
        var model = Assert.IsType<MediaDeleteUnusedViewModel>(view.Model);
        Assert.Equal(["a.png", "b.png"], model.Items.Select(i => i.FileName));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("charts", model.Search);
    }

    [Fact]
    public async Task DeleteUnused_DeletesThePostedImages_AndSaysWhatHappened()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        _deletions.DeleteUnusedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new MediaUnusedDeleteResult(Guid.NewGuid(), [ids[0], ids[1]], [ids[2]], []));

        var redirect = Assert.IsType<RedirectResult>(await _sut.DeleteUnused([.. ids], "charts", ct: CancellationToken.None));

        await _deletions.Received(1).DeleteUnusedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(posted => posted.SequenceEqual(ids)), Arg.Any<CancellationToken>());
        Assert.Equal("/admin/media?search=charts&unused=true", redirect.Url);
        Assert.Equal("2 unused images deleted. 1 image was not deleted, because content now uses it.",
            _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task DeleteUnused_SaysOneImage_InTheSingular()
    {
        var id = Guid.NewGuid();
        _deletions.DeleteUnusedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new MediaUnusedDeleteResult(Guid.NewGuid(), [id], [], []));

        var redirect = Assert.IsType<RedirectResult>(await _sut.DeleteUnused([id], null, ct: CancellationToken.None));

        Assert.Equal("/admin/media?unused=true", redirect.Url);
        Assert.Equal("1 unused image deleted.", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task Delete_GoesBackToTheLibrary_WithASuccessMessage()
    {
        var asset = Known();
        _deletions.DeleteAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.Deleted);

        var redirect = Assert.IsType<RedirectResult>(await _sut.Delete(asset.Id, ct: CancellationToken.None));

        Assert.Equal("/admin/media", redirect.Url);
        Assert.Equal($"{asset.FileName} deleted", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    // The server checks again: a page may have started to use the image since the page was shown.
    [Fact]
    public async Task Delete_OfAnImageNowInUse_ShowsThePagesInstead()
    {
        var asset = Known();
        _deletions.DeleteAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.InUse);
        _media.GetUsagePagesAsync(asset.Id, Arg.Any<CancellationToken>()).Returns([new MediaUsagePage(Guid.NewGuid(), "Attendance", "attendance", false, true)]);

        var view = Assert.IsType<ViewResult>(await _sut.Delete(asset.Id, ct: CancellationToken.None));

        Assert.Equal("Delete", view.ViewName);
        var model = Assert.IsType<MediaDeleteViewModel>(view.Model);
        Assert.False(model.CanDelete);
        Assert.Equal("1 page", model.Usage.UsedOn);
        Assert.Equal(409, _sut.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Delete_IsNotFound_ForAnUnknownImage()
    {
        var id = Guid.NewGuid();
        _deletions.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(MediaDeleteOutcome.NotFound);

        Assert.IsType<NotFoundResult>(await _sut.Delete(id, ct: CancellationToken.None));
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
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(created));
        _uploads.UploadAsync(Arg.Any<Stream>(), "copy.png", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Duplicate(existing));
        _uploads.UploadAsync(Arg.Any<Stream>(), "notes.txt", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Rejected(MediaRejections.NotAnImage));

        var view = Assert.IsType<ViewResult>(await _sut.Upload([File("new.png"), File("copy.png"), File("notes.txt")], ct: CancellationToken.None));

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
        var view = Assert.IsType<ViewResult>(await _sut.Upload([], ct: CancellationToken.None));

        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(MediaLibraryController.NoFileError, model.UploadError);
        Assert.Equal(400, _sut.HttpContext.Response.StatusCode);
        await _uploads.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Upload_AnswersJson_WhenTheScriptAsksForIt()
    {
        AcceptJson();
        var created = Asset("new.png");
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Created(created));

        var json = Assert.IsType<JsonResult>(await _sut.Upload([File("new.png")], ct: CancellationToken.None));

        var results = Assert.IsAssignableFrom<IReadOnlyList<MediaUploadItemViewModel>>(json.Value);
        Assert.Equal("created", Assert.Single(results).OutcomeName);
    }

    [Fact]
    public async Task Upload_AnswersJson400_WhenTheScriptSendsNoFile()
    {
        AcceptJson();

        var json = Assert.IsType<JsonResult>(await _sut.Upload([], ct: CancellationToken.None));

        Assert.Equal(400, json.StatusCode);
    }

    // ---- Folders ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_InAFolder_ListsThatFolder_WithOrWithoutSubfolders(bool subfolders)
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, null, 1, Photos, subfolders, CancellationToken.None));

        await _media.Received(1).ListAsync(
            Arg.Is<MediaAssetQuery>(q => q.FolderId == Photos && q.IncludeSubfolders == subfolders), Arg.Any<CancellationToken>());
        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(Photos, model.Folder!.Id);
        Assert.Equal(subfolders, model.IncludeSubfolders);
        Assert.Equal(["Schools"], model.Subfolders.Select(f => f.Name));
    }

    [Fact]
    public async Task Index_ByDefault_ListsTheWholeLibrary()
    {
        await _sut.Index(null, false, false, null, null, ct: CancellationToken.None);

        await _media.Received(1).ListAsync(Arg.Is<MediaAssetQuery>(q => q.FolderId == null && q.IncludeSubfolders), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_ForAFolderThatDoesNotExist_IsNotFound()
    {
        Assert.IsType<NotFoundResult>(await _sut.Index(null, false, false, null, null, 1, Guid.NewGuid(), true, CancellationToken.None));
    }

    [Fact]
    public async Task Index_GivesEachImage_ItsFolderPath()
    {
        var inSchools = Asset() with { FolderId = Schools };
        var atTop = Asset("top.png");
        _media.ListAsync(Arg.Any<MediaAssetQuery>(), Arg.Any<CancellationToken>()).Returns(new MediaAssetPage([inSchools, atTop], 2, 1, 25));
        _media.CountUsesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        var view = Assert.IsType<ViewResult>(await _sut.Index(null, false, false, null, null, ct: CancellationToken.None));

        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(["Photos / Schools", ""], model.Items.Select(i => i.Folder));
    }

    [Fact]
    public async Task CreateFolder_GoesBackToTheParentFolder_WithAMessage()
    {
        _folders.CreateAsync("Logos", Photos, Arg.Any<CancellationToken>())
            .Returns(MediaFolderResult.Done(new MediaFolderDto(Guid.NewGuid(), "Logos", Photos)));

        var redirect = Assert.IsType<RedirectResult>(await _sut.CreateFolder("Logos", Photos, CancellationToken.None));

        Assert.Equal($"/admin/media?folder={Photos:D}", redirect.Url);
        Assert.Equal("Folder ‘Logos’ created", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task CreateFolder_Refused_ShowsTheLibraryAgain_WithTheErrorAndTheName()
    {
        _folders.CreateAsync("Schools", Photos, Arg.Any<CancellationToken>())
            .Returns(MediaFolderResult.Refused(MediaFolderOutcome.NameTaken, MediaFolderNames.Taken("Schools")));

        var view = Assert.IsType<ViewResult>(await _sut.CreateFolder("Schools", Photos, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, _sut.Response.StatusCode);
        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Equal(Photos, model.Folder!.Id);
        Assert.Equal("Schools", model.NewFolderName);
        Assert.Equal(MediaFolderNames.Taken("Schools"), model.NewFolderError);
    }

    [Fact]
    public async Task Folder_ShowsItsCounts_AndWhereItCanMove()
    {
        _folders.CountContentsAsync(Photos, Arg.Any<CancellationToken>()).Returns((3, 1));

        var view = Assert.IsType<ViewResult>(await _sut.Folder(Photos, CancellationToken.None));

        var model = Assert.IsType<MediaFolderViewModel>(view.Model);
        Assert.Equal(3, model.ImageCount);
        Assert.False(model.CanDelete);
        Assert.Empty(model.MoveTargets); // only the folder itself and its own subfolder exist
        Assert.IsType<NotFoundResult>(await _sut.Folder(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task MoveFolder_IntoItsOwnSubfolder_ShowsTheError()
    {
        _folders.MoveAsync(Photos, Schools, Arg.Any<CancellationToken>())
            .Returns(MediaFolderResult.Refused(MediaFolderOutcome.Cycle, MediaFolderService.CycleError));

        var view = Assert.IsType<ViewResult>(await _sut.MoveFolder(Photos, Schools.ToString(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, _sut.Response.StatusCode);
        Assert.Equal(MediaFolderService.CycleError, Assert.IsType<MediaFolderViewModel>(view.Model).MoveError);
    }

    [Fact]
    public async Task MoveFolder_ToTheTop_UsesNoParent()
    {
        _folders.MoveAsync(Schools, null, Arg.Any<CancellationToken>()).Returns(MediaFolderResult.Done(new MediaFolderDto(Schools, "Schools", null)));

        var redirect = Assert.IsType<RedirectResult>(await _sut.MoveFolder(Schools, MediaLibraryController.TopLevel, CancellationToken.None));

        Assert.Equal($"/admin/media/folders/{Schools:D}", redirect.Url);
    }

    [Fact]
    public async Task MoveFolder_WithNoChoice_AsksForOne()
    {
        var view = Assert.IsType<ViewResult>(await _sut.MoveFolder(Schools, "", CancellationToken.None));

        Assert.Equal(MediaLibraryController.NoFolderTargetError, Assert.IsType<MediaFolderViewModel>(view.Model).MoveError);
        await _folders.DidNotReceiveWithAnyArgs().MoveAsync(default, default);
    }

    [Fact]
    public async Task DeleteFolder_ThatHoldsThings_SaysWhy_WithAConflict()
    {
        _folders.DeleteAsync(Photos, Arg.Any<CancellationToken>())
            .Returns(MediaFolderResult.Refused(MediaFolderOutcome.NotEmpty, "You cannot delete ‘Photos’"));

        var view = Assert.IsType<ViewResult>(await _sut.DeleteFolder(Photos, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, _sut.Response.StatusCode);
        Assert.Equal("You cannot delete ‘Photos’", Assert.IsType<MediaFolderViewModel>(view.Model).DeleteError);
    }

    [Fact]
    public async Task DeleteFolder_GoesToTheParentFolder()
    {
        _folders.DeleteAsync(Schools, Arg.Any<CancellationToken>()).Returns(MediaFolderResult.Done(new MediaFolderDto(Schools, "Schools", Photos)));

        var redirect = Assert.IsType<RedirectResult>(await _sut.DeleteFolder(Schools, CancellationToken.None));

        Assert.Equal($"/admin/media?folder={Photos:D}", redirect.Url);
        Assert.Equal("Folder ‘Schools’ deleted", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task MoveImages_MovesTheTickedImages_AndGoesBackToTheList()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _folders.MoveImagesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Schools, Arg.Any<CancellationToken>())
            .Returns(new MediaImageMoveResult(MediaFolderOutcome.Done, 2));

        var back = $"/admin/media?folder={Photos:D}&view=details&page=2";
        var redirect = Assert.IsType<RedirectResult>(await _sut.MoveImages(ids, Schools.ToString(), back, CancellationToken.None));

        Assert.Equal(back, redirect.Url);
        await _folders.Received(1).MoveImagesAsync(Arg.Is<IReadOnlyCollection<Guid>>(c => c.SequenceEqual(ids)), Schools, Arg.Any<CancellationToken>());
        Assert.Equal("2 images moved to ‘Photos / Schools’", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Theory]
    [InlineData("https://evil.example/admin/media")]
    [InlineData("//evil.example")]
    [InlineData("/admin/mediabad")]
    [InlineData(null)]
    public void MoveImages_GoesOnlyToALibraryAddress(string? returnUrl)
    {
        Assert.Equal("/admin/media", MediaLibraryController.SafeReturnUrl(returnUrl));
    }

    [Fact]
    public async Task MoveImages_WithNoFolderChosen_ShowsAnErrorOnTheList()
    {
        var redirect = Assert.IsType<RedirectResult>(await _sut.MoveImages([Guid.NewGuid()], "", "/admin/media", CancellationToken.None));

        Assert.Equal("/admin/media", redirect.Url);
        Assert.Equal(MediaLibraryController.NoMoveTargetError, _sut.TempData[MediaLibraryController.MoveErrorKey]);
        await _folders.DidNotReceiveWithAnyArgs().MoveImagesAsync(default!, default);
    }

    [Fact]
    public async Task MoveImages_ToTheTop_UsesNoFolder()
    {
        _folders.MoveImagesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new MediaImageMoveResult(MediaFolderOutcome.Done, 1));

        await _sut.MoveImages([Guid.NewGuid()], MediaLibraryController.TopLevel, "/admin/media", CancellationToken.None);

        Assert.Equal("1 image moved to the top of the library", _sut.TempData[MediaLibraryController.SuccessKey]);
    }

    [Fact]
    public async Task Upload_PutsTheFilesInTheFolderShown()
    {
        AcceptJson();
        _uploads.UploadAsync(Arg.Any<Stream>(), "new.png", Photos, Arg.Any<CancellationToken>()).Returns(MediaUploadResult.Rejected("x"));

        await _sut.Upload([File("new.png")], Photos, CancellationToken.None);

        await _uploads.Received(1).UploadAsync(Arg.Any<Stream>(), "new.png", Photos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUnused_GoesBackToTheSameFolder()
    {
        _deletions.DeleteUnusedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new MediaUnusedDeleteResult(Guid.NewGuid(), [], [], []));

        var redirect = Assert.IsType<RedirectResult>(await _sut.DeleteUnused([], "logo", Photos, false, CancellationToken.None));

        Assert.Equal($"/admin/media?folder={Photos:D}&subfolders=false&search=logo&unused=true", redirect.Url);
    }

    [Fact]
    public async Task DeleteUnusedConfirm_ListsOnlyTheFolderAsked()
    {
        await _sut.DeleteUnusedConfirm("logo", Photos, false, CancellationToken.None);

        await _media.Received(1).ListAsync(
            Arg.Is<MediaAssetQuery>(q => q.UnusedOnly && q.FolderId == Photos && !q.IncludeSubfolders), Arg.Any<CancellationToken>());
    }
}
