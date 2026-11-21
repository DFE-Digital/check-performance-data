using System.Reflection;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The admin action that moves images embedded in page content into the media library: a dry run
// that changes nothing, and a run that needs a posted form.
public sealed class MediaEmbeddedImagesControllerTests
{
    private readonly IEmbeddedImageMover _mover = Substitute.For<IEmbeddedImageMover>();
    private readonly MediaEmbeddedImagesController _sut;

    public MediaEmbeddedImagesControllerTests() => _sut = new MediaEmbeddedImagesController(_mover);

    [Fact]
    public void TheController_IsGatedOnTheMediaSection()
    {
        var gate = typeof(MediaEmbeddedImagesController).GetCustomAttribute<RequireAdminSectionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.Media, gate!.SectionKey);
    }

    [Fact]
    public async Task TheDryRun_ShowsTheScan_AndMovesNothing()
    {
        var scan = new EmbeddedImageScan([], 0, 0, 0, 0, 2, []);
        _mover.ScanAsync(Arg.Any<CancellationToken>()).Returns(scan);

        var result = await _sut.DryRun(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(MediaEmbeddedImagesController.DryRunView, view.ViewName);
        Assert.Same(scan, view.Model);
        await _mover.DidNotReceive().MoveAsync(Arg.Any<CancellationToken>());
        Assert.Equal("admin/media/move-embedded", typeof(MediaEmbeddedImagesController)
            .GetMethod(nameof(MediaEmbeddedImagesController.DryRun))!.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public async Task TheRun_MovesTheImages_AndShowsWhatItDid()
    {
        var moved = new EmbeddedImageMoveResult(1, 2, 2, 0, [], 0, []);
        _mover.MoveAsync(Arg.Any<CancellationToken>()).Returns(moved);

        var result = await _sut.Run(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(MediaEmbeddedImagesController.ResultView, view.ViewName);
        Assert.Same(moved, view.Model);
    }

    [Fact]
    public void TheRun_IsAnAntiforgeryCheckedPost()
    {
        var method = typeof(MediaEmbeddedImagesController).GetMethod(nameof(MediaEmbeddedImagesController.Run))!;

        Assert.Equal("admin/media/move-embedded", method.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
