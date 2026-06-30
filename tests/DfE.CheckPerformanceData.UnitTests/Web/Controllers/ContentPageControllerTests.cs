using System.Reflection;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The editor controller is thin: parse the form path, call the editor/service, redirect back (PRG).
// These tests pin that wiring plus the security contract (editor-role gating + antiforgery on every
// POST), which is the part most costly to get wrong.
public sealed class ContentPageControllerTests
{
    private readonly IContentPageService _pages = Substitute.For<IContentPageService>();
    private readonly IContentPageEditor _editor = Substitute.For<IContentPageEditor>();

    private ContentPageController Sut() => new(_pages, _editor);

    private static bool IsPath(IReadOnlyList<TreeStep> steps, params TreeStep[] expected) =>
        steps.SequenceEqual(expected);

    [Fact]
    public void New_ReturnsView()
    {
        Assert.IsType<ViewResult>(Sut().New());
    }

    [Fact]
    public async Task Index_ReturnsView_WithAllPageSummaries()
    {
        _pages.GetAllAsync().Returns([
            new ContentPageSummaryDto { Slug = "a", Title = "Alpha", Layout = "nav-content", PublishedVersionNumber = 2 },
            new ContentPageSummaryDto { Slug = "b", Title = "Beta", Layout = "full" }
        ]);

        var view = Assert.IsType<ViewResult>(await Sut().Index());
        var model = Assert.IsType<List<ContentPageSummaryDto>>(view.Model);
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public async Task Create_CreatesPage_AndRedirectsToEditor()
    {
        var result = await Sut().Create(new NewContentPageModel { Title = "KS4 checking", Layout = "nav-content" });

        await _pages.Received(1).CreateAsync("ks4-checking", "KS4 checking", null, "nav-content");
        Assert.Equal("/guidance/ks4-checking/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task Create_DerivesSlugFromExplicitSlugField_WhenProvided()
    {
        await Sut().Create(new NewContentPageModel { Title = "Anything", Slug = "Custom Slug" });

        await _pages.Received(1).CreateAsync("custom-slug", "Anything", null, "nav-content");
    }

    [Fact]
    public async Task Edit_ReturnsNotFound_WhenPageMissing()
    {
        _pages.GetBySlugAsync("missing").Returns((ContentPageDto?)null);

        Assert.IsType<NotFoundResult>(await Sut().Edit("missing"));
    }

    [Fact]
    public async Task Edit_ReturnsView_WithDeserialisedDraft()
    {
        _pages.GetBySlugAsync("ks4").Returns(new ContentPageDto
        {
            Id = 1,
            Slug = "ks4",
            Title = "KS4",
            Layout = "nav-content",
            DraftContent = """[{"kind":"widget","type":"divider"}]"""
        });

        var view = Assert.IsType<ViewResult>(await Sut().Edit("ks4"));
        var model = Assert.IsType<ContentPageEditViewModel>(view.Model);
        Assert.Single(model.Content);
    }

    [Fact]
    public async Task Add_Widget_CallsEditor_WithParsedPath()
    {
        var result = await Sut().Add("ks4", "0.0", widgetType: "heading", regionLayout: null);

        await _editor.Received(1).AddWidgetAsync("ks4", Arg.Is<IReadOnlyList<TreeStep>>(p => IsPath(p, new TreeStep(0, 0))), "heading");
        Assert.Equal("/guidance/ks4/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task Add_Region_CallsEditor_WithParsedLayout()
    {
        await Sut().Add("ks4", "0.1", widgetType: null, regionLayout: "Halves");

        await _editor.Received(1).AddRegionAsync("ks4", Arg.Is<IReadOnlyList<TreeStep>>(p => IsPath(p, new TreeStep(0, 1))), RegionLayout.Halves);
    }

    [Fact]
    public async Task Move_Up_CallsEditorMoveUp()
    {
        await Sut().Move("ks4", "0.2", "up");

        await _editor.Received(1).MoveUpAsync("ks4", Arg.Is<IReadOnlyList<TreeStep>>(p => IsPath(p, new TreeStep(0, 2))));
    }

    [Fact]
    public async Task Move_Down_CallsEditorMoveDown()
    {
        await Sut().Move("ks4", "0.0", "down");

        await _editor.Received(1).MoveDownAsync("ks4", Arg.Any<IReadOnlyList<TreeStep>>());
    }

    [Fact]
    public async Task Delete_CallsEditorDelete()
    {
        await Sut().Delete("ks4", "0.0");

        await _editor.Received(1).DeleteAsync("ks4", Arg.Any<IReadOnlyList<TreeStep>>());
    }

    [Fact]
    public async Task UpdateWidget_BuildsProps_AndCallsEditor()
    {
        await Sut().UpdateWidget("ks4", "0.0", "heading",
            new Dictionary<string, string?> { ["level"] = "2", ["text"] = "Key dates" });

        await _editor.Received(1).UpdateWidgetAsync(
            "ks4",
            Arg.Any<IReadOnlyList<TreeStep>>(),
            Arg.Is<System.Text.Json.Nodes.JsonObject>(p => (string)p["text"]! == "Key dates"));
    }

    [Fact]
    public async Task Publish_PublishesAndRedirectsToPublicPage()
    {
        var result = await Sut().Publish("ks4");

        await _pages.Received(1).PublishAsync("ks4");
        Assert.Equal("/guidance/ks4", Assert.IsType<RedirectResult>(result).Url);
    }

    // --- Security contract ---

    [Fact]
    public void Controller_IsGatedToTheEditorRole()
    {
        var authorize = typeof(ContentPageController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(WikiConstants.EditorRole, authorize!.Roles);
    }

    [Theory]
    [InlineData(nameof(ContentPageController.Create))]
    [InlineData(nameof(ContentPageController.Add))]
    [InlineData(nameof(ContentPageController.Move))]
    [InlineData(nameof(ContentPageController.Delete))]
    [InlineData(nameof(ContentPageController.UpdateWidget))]
    [InlineData(nameof(ContentPageController.Publish))]
    public void EveryMutatingPost_ValidatesAntiforgery(string actionName)
    {
        var method = typeof(ContentPageController).GetMethod(actionName)!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }
}
