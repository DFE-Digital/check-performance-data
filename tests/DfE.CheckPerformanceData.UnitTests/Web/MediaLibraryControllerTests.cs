using System.Reflection;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaLibraryControllerTests
{
    private readonly IMediaAssetRepository _media = Substitute.For<IMediaAssetRepository>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly MediaLibraryController _sut;

    public MediaLibraryControllerTests()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(25);
        _sut = new MediaLibraryController(_media, _settings);
    }

    [Fact]
    public void Controller_IsGatedByTheMediaSection()
    {
        var gate = typeof(MediaLibraryController).GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.Media, gate!.SectionKey);
    }

    [Fact]
    public async Task Index_ListsOnePageOfTheLibrary_AtTheCmsPageLength()
    {
        var page = new MediaAssetPage([], 0, 1, 25);
        _media.ListAsync(Arg.Is<MediaAssetQuery>(q => q.Page == 2 && q.PageSize == 25), Arg.Any<CancellationToken>()).Returns(page);

        var view = Assert.IsType<ViewResult>(await _sut.Index(page: 2, CancellationToken.None));

        var model = Assert.IsType<MediaLibraryIndexViewModel>(view.Model);
        Assert.Same(page, model.Page);
    }
}
