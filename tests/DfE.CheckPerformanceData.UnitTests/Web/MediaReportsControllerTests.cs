using System.Reflection;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The media library reports: alt text that does not describe its image, and storage size. Both
// are read-only pages on the media section. The alt text report pages its two lists separately.
public sealed class MediaReportsControllerTests
{
    private readonly IMediaReportService _reports = Substitute.For<IMediaReportService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly MediaReportsController _sut;

    public MediaReportsControllerTests()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(2);
        _sut = new MediaReportsController(_reports, _settings);
    }

    private static MediaAltTextAssetRow AssetRow(int n) => new(Guid.NewGuid(), $"file-{n}.png", null, AltTextProblem.Missing);

    private static MediaAltTextUseRow UseRow(int n) =>
        new(Guid.NewGuid(), "a.png", Guid.NewGuid(), null, $"Page {n}", [EmbeddedVersionState.Live], MediaUsageLocations.Widget, "image", false, AltTextProblem.Generic);

    [Fact]
    public void TheController_IsGatedOnTheMediaSection()
    {
        var gate = typeof(MediaReportsController).GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.Media, gate!.SectionKey);
    }

    [Theory]
    [InlineData(nameof(MediaReportsController.AltText), "admin/media/reports/alt-text")]
    [InlineData(nameof(MediaReportsController.Size), "admin/media/reports/size")]
    public void EachReport_IsAGetOnItsOwnRoute(string action, string route)
    {
        var method = typeof(MediaReportsController).GetMethod(action)!;

        Assert.Equal(route, method.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact]
    public async Task TheAltTextReport_PagesEachListOnItsOwn_AtTheCmsPageLength()
    {
        _reports.GetAltTextReportAsync(Arg.Any<CancellationToken>()).Returns(new MediaAltTextReport(
            [AssetRow(1), AssetRow(2), AssetRow(3)],
            [UseRow(1), UseRow(2), UseRow(3), UseRow(4), UseRow(5)]));

        var result = await _sut.AltText(imagesPage: 2, usesPage: 3, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(MediaReportsController.AltTextView, view.ViewName);
        var model = Assert.IsType<MediaAltTextReportViewModel>(view.Model);
        Assert.Equal(["file-3.png"], model.Images.Items.Select(r => r.FileName));
        Assert.Equal((2, 2, 3), (model.Images.Page, model.Images.TotalPages, model.Images.Total));
        Assert.Equal(["Page 5"], model.Uses.Items.Select(r => r.OwnerName));
        Assert.Equal((3, 3, 5), (model.Uses.Page, model.Uses.TotalPages, model.Uses.Total));
    }

    [Fact]
    public async Task APageOutOfRange_ShowsTheNearestPage()
    {
        _reports.GetAltTextReportAsync(Arg.Any<CancellationToken>()).Returns(new MediaAltTextReport([AssetRow(1)], []));

        var result = await _sut.AltText(imagesPage: 9, usesPage: -1, CancellationToken.None);

        var model = Assert.IsType<MediaAltTextReportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal((1, 1), (model.Images.Page, model.Images.TotalPages));
        Assert.Equal((1, 1, 0), (model.Uses.Page, model.Uses.TotalPages, model.Uses.Total));
        Assert.Single(model.Images.Items);
    }

    [Fact]
    public async Task TheSizeReport_ShowsTheLargestImages_UpToTheCap()
    {
        var report = new MediaSizeReport(new MediaSizeTotals(0, 0, 0, 0), MediaRenditionTally.None, []);
        _reports.GetSizeReportAsync(MediaReportsController.LargestCount, Arg.Any<CancellationToken>()).Returns(report);

        var result = await _sut.Size(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(MediaReportsController.SizeView, view.ViewName);
        Assert.Same(report, view.Model);
    }
}
