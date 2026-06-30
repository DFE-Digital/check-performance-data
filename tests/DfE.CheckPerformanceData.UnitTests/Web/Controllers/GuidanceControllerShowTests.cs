using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The catch-all /guidance/{slug} action serves a database-backed page from its latest published
// version: an unknown slug or a never-published page is a 404; a published page renders with its
// deserialised content and the auto-generated nav built from its headings.
public sealed class GuidanceControllerShowTests
{
    private readonly IContentPageService _service = Substitute.For<IContentPageService>();

    private GuidanceController CreateSut() => new(_service);

    private static ContentPageDto Page(int? publishedVersion) => new()
    {
        Id = 1,
        Slug = "ks4",
        Title = "KS4 checking",
        Caption = "For the 2026 cycle",
        Layout = "nav-content",
        DraftContent = "[]",
        PublishedVersionNumber = publishedVersion
    };

    [Fact]
    public async Task Show_ReturnsNotFound_WhenSlugDoesNotExist()
    {
        _service.GetBySlugAsync("missing").Returns((ContentPageDto?)null);

        var result = await CreateSut().Show("missing");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsNotFound_WhenPageHasNeverBeenPublished()
    {
        _service.GetBySlugAsync("ks4").Returns(Page(publishedVersion: null));

        var result = await CreateSut().Show("ks4");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsViewWithModelAndAutoNav_WhenPublished()
    {
        _service.GetBySlugAsync("ks4").Returns(Page(publishedVersion: 2));
        _service.GetPublishedContentAsync("ks4").Returns("""
            [
              {"kind":"widget","type":"heading","anchor":"key-dates","props":{"level":2,"text":"Key dates"}},
              {"kind":"widget","type":"heading","anchor":"get-support","props":{"level":2,"text":"Get support"}}
            ]
            """);

        var result = await CreateSut().Show("ks4");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ContentPageViewModel>(view.Model);
        Assert.Equal("KS4 checking", model.Title);
        Assert.Equal("nav-content", model.Layout);
        Assert.Equal(2, model.Content.Count);
        Assert.Equal(["Key dates", "Get support"], model.Nav.Select(n => n.Text));
    }
}
