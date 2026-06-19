using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The admin-only token management surface. Generation must defend the varchar(200) label column
// (an over-long label is capped, never a DbUpdateException 500) and reject a surface value that
// is not a defined ShareSurface (model binding would otherwise silently coerce an arbitrary
// integer to default(ShareSurface) and mint a token for the wrong surface).
public sealed class ShareAdminControllerTests
{
    private static IShareTokenService TokenService()
    {
        var tokens = Substitute.For<IShareTokenService>();
        tokens.GenerateAsync(
                Arg.Any<string>(), Arg.Any<ShareSurface>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("plaintext-token");
        return tokens;
    }

    // --- An over-long label is truncated to the 200-char column limit, not allowed to 500 ---

    [Fact]
    public async Task GenerateShareToken_OverLongLabel_IsTruncatedTo200Chars()
    {
        var tokens = TokenService();
        var controller = new ShareAdminController(tokens);

        var result = await controller.GenerateShareToken(new string('x', 500), ShareSurface.Share);

        Assert.IsType<RedirectToActionResult>(result);
        await tokens.Received(1).GenerateAsync(
            Arg.Is<string>(label => label.Length == 200),
            ShareSurface.Share,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateShareToken_LabelAtTheLimit_PassesThroughUnchanged()
    {
        var tokens = TokenService();
        var controller = new ShareAdminController(tokens);
        var label = new string('y', 200);

        await controller.GenerateShareToken(label, ShareSurface.Wallboard);

        await tokens.Received(1).GenerateAsync(
            label, ShareSurface.Wallboard, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- A surface value outside the enum is rejected with 400, and no token is minted ---

    [Fact]
    public async Task GenerateShareToken_UndefinedSurface_ReturnsBadRequest_AndMintsNothing()
    {
        var tokens = TokenService();
        var controller = new ShareAdminController(tokens);

        var result = await controller.GenerateShareToken("A label", (ShareSurface)999);

        Assert.IsType<BadRequestObjectResult>(result);
        await tokens.DidNotReceive().GenerateAsync(
            Arg.Any<string>(), Arg.Any<ShareSurface>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ShareSurface.Share)]
    [InlineData(ShareSurface.Wallboard)]
    public async Task GenerateShareToken_DefinedSurface_IsAccepted(ShareSurface surface)
    {
        var tokens = TokenService();
        var controller = new ShareAdminController(tokens);

        var result = await controller.GenerateShareToken("A label", surface);

        Assert.IsType<RedirectToActionResult>(result);
    }
}
