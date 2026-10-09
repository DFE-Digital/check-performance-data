using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.HomeBanners;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class HomeBannersControllerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0);
    private readonly IHomeBannerService _service = Substitute.For<IHomeBannerService>();
    private readonly HomeBannersController _sut;

    public HomeBannersControllerTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _sut = new HomeBannersController(_service, clock);
    }

    [Fact]
    public async Task Index_ListsEveryBanner_WithNow()
    {
        var banners = new List<HomeBannerDto> { new() { Id = 1, Heading = "A" }, new() { Id = 2, Heading = "B" } };
        _service.GetAllAsync().Returns(banners);

        var view = Assert.IsType<ViewResult>(await _sut.Index());
        var model = Assert.IsType<HomeBannersIndexViewModel>(view.Model);
        Assert.Equal(banners, model.Banners);
        Assert.Equal(Now, model.Now);
    }

    [Fact]
    public async Task Create_WhenValid_SavesAndRedirectsToTheList()
    {
        _service.CreateAsync(Arg.Any<HomeBannerContent>()).Returns(new HomeBannerDto { Id = 3 });
        var model = new HomeBannerFormModel { Heading = "New rules", Body = "<p>x</p>", IsEnabled = true };

        var result = Assert.IsType<RedirectResult>(await _sut.Create(model));

        Assert.Equal("/admin/home-banners", result.Url);
        await _service.Received(1).CreateAsync(Arg.Is<HomeBannerContent>(c => c.Heading == "New rules" && c.IsEnabled));
    }

    [Fact]
    public async Task Create_WhenShowUntilIsNotAfterShowFrom_RedisplaysTheForm()
    {
        var model = new HomeBannerFormModel
        {
            Heading = "H", Body = "b",
            ShowFromDate = new DateTime(2026, 10, 10), ShowFromHour = 9, ShowFromMinute = 0,
            ShowUntilDate = new DateTime(2026, 10, 10), ShowUntilHour = 9, ShowUntilMinute = 0
        };

        var view = Assert.IsType<ViewResult>(await _sut.Create(model));

        Assert.Equal("Edit", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey(nameof(HomeBannerFormModel.ShowUntilDate)));
        await _service.DidNotReceive().CreateAsync(Arg.Any<HomeBannerContent>());
    }

    [Fact]
    public async Task Create_WhenATimeIsGivenWithoutADate_RedisplaysTheForm()
    {
        var model = new HomeBannerFormModel { Heading = "H", Body = "b", ShowFromHour = 9 };

        var view = Assert.IsType<ViewResult>(await _sut.Create(model));

        Assert.Equal("Edit", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey(nameof(HomeBannerFormModel.ShowFromDate)));
    }

    [Fact]
    public async Task Edit_UnknownId_Returns404()
    {
        _service.GetByIdAsync(9).ReturnsNull();
        Assert.IsType<NotFoundResult>(await _sut.Edit(9));
    }

    [Fact]
    public async Task Update_WhenValid_SavesAndRedirectsToTheList()
    {
        _service.UpdateAsync(5, Arg.Any<HomeBannerContent>()).Returns(new HomeBannerDto { Id = 5 });
        var model = new HomeBannerFormModel { Id = 5, Heading = "H", Body = "b" };

        var result = Assert.IsType<RedirectResult>(await _sut.Update(5, model));

        Assert.Equal("/admin/home-banners", result.Url);
    }

    [Fact]
    public async Task Move_RejectsAnythingButUpOrDown()
    {
        Assert.IsType<BadRequestResult>(await _sut.Move(1, "sideways"));
        await _service.DidNotReceive().MoveAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeletePost_UnknownId_Returns404()
    {
        _service.DeleteAsync(1).Returns(false);
        Assert.IsType<NotFoundResult>(await _sut.DeletePost(1));
    }

    [Fact]
    public async Task Restore_UnknownVersion_Returns404()
    {
        _service.RestoreVersionAsync(1, 2).ReturnsNull();
        Assert.IsType<NotFoundResult>(await _sut.Restore(1, 2));
    }
}
