using System.Reflection;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Verifies the catch-all page resolver:
//   - unknown path or path with no live version → 404
//   - content node → View("Content", RenderedPageViewModel) with deserialised tree + auto nav
//   - wiki node   → View("Wiki",    RenderedPageViewModel) with raw HTML
//   - folder node → View("Folder",  RenderedPageViewModel)
//   - [HttpGet("/{*path}", Order = int.MaxValue)] enforced via reflection
public sealed class PageControllerTests
{
    private readonly IPageNodeService _pageNodes = Substitute.For<IPageNodeService>();

    private PageController CreateSut() => new(_pageNodes);

    private static PageNodeDto Node(string pageType, string title = "Test Page") => new()
    {
        Segment = "test",
        Path = "test",
        Title = title,
        PageType = pageType
    };

    private static LivePageResult Live(string pageType, string content, string title = "Test Page") => new()
    {
        Node = Node(pageType, title),
        Version = new PageNodeVersionDto { Content = content }
    };

    [Fact]
    public async Task Show_ReturnsNotFound_WhenPathDoesNotExist()
    {
        _pageNodes.GetLivePageAsync("unknown", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);

        var result = await CreateSut().Show("unknown");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsNotFound_WhenNodeHasNoLiveVersion()
    {
        // IPageNodeService returns null when no version is currently live (same code path as
        // path-not-found — the controller simply sees null and returns 404 in both cases).
        _pageNodes.GetLivePageAsync("unpublished", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);

        var result = await CreateSut().Show("unpublished");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsContentView_WithDeserialisedTreeAndNav()
    {
        const string json = """
            [
              {"kind":"widget","type":"heading","anchor":"intro","props":{"level":2,"text":"Introduction"}},
              {"kind":"widget","type":"heading","anchor":"summary","props":{"level":2,"text":"Summary"}}
            ]
            """;
        _pageNodes.GetLivePageAsync("my-page", Arg.Any<DateTime>())
            .Returns(Live("content", json, "My Page"));

        var result = await CreateSut().Show("my-page");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Content", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("My Page", model.Title);
        Assert.Equal("content", model.PageType);
        Assert.NotNull(model.Content);
        Assert.Equal(2, model.Content!.Count);
        Assert.NotNull(model.Nav);
        Assert.Equal(["Introduction", "Summary"], model.Nav!.Select(n => n.Text));
    }

    [Fact]
    public async Task Show_ReturnsWikiView_WithRawHtml()
    {
        _pageNodes.GetLivePageAsync("wiki-page", Arg.Any<DateTime>())
            .Returns(Live("wiki", "<p>Hello world</p>", "Wiki Page"));

        var result = await CreateSut().Show("wiki-page");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Wiki", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("Wiki Page", model.Title);
        Assert.Equal("wiki", model.PageType);
        Assert.Equal("<p>Hello world</p>", model.WikiHtml);
    }

    [Fact]
    public async Task Show_ReturnsFolderView_WithTitle()
    {
        _pageNodes.GetLivePageAsync("support", Arg.Any<DateTime>())
            .Returns(Live("folder", string.Empty, "Support"));

        var result = await CreateSut().Show("support");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Folder", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("Support", model.Title);
        Assert.Equal("folder", model.PageType);
    }

    [Fact]
    public async Task Show_ReturnsWikiView_WithSiblingNavOrderedBySortOrder()
    {
        var parentId = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();

        _pageNodes.GetLivePageAsync("section/wiki-page", Arg.Any<DateTime>())
            .Returns(new LivePageResult
            {
                Node = new PageNodeDto
                {
                    Id       = nodeId,
                    ParentId = parentId,
                    Segment  = "wiki-page",
                    Path     = "section/wiki-page",
                    Title    = "Wiki Page",
                    PageType = "wiki"
                },
                Version = new PageNodeVersionDto { Content = "<p>Body</p>" }
            });

        _pageNodes.GetTreeAsync()
            .Returns(new List<PageNodeTreeItemDto>
            {
                // unrelated root-level item — must be excluded
                new() { Id = Guid.NewGuid(), ParentId = null,     Segment = "other-root", Path = "other-root",        SortOrder = 0, Title = "Other Root", PageType = "wiki" },
                // siblings, intentionally out of SortOrder to verify ordering
                new() { Id = nodeId,         ParentId = parentId, Segment = "wiki-page",  Path = "section/wiki-page", SortOrder = 1, Title = "Wiki Page",  PageType = "wiki" },
                new() { Id = Guid.NewGuid(), ParentId = parentId, Segment = "first",      Path = "section/first",     SortOrder = 0, Title = "First",      PageType = "wiki" }
            });

        var result = await CreateSut().Show("section/wiki-page");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Wiki", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);

        // Both siblings returned; root-level item excluded.
        Assert.NotNull(model.Nav);
        Assert.Equal(2, model.Nav!.Count);

        // Ordered by SortOrder ascending.
        Assert.Equal("First",     model.Nav[0].Text);
        Assert.Equal("/section/first",     model.Nav[0].Href);
        Assert.Equal("Wiki Page", model.Nav[1].Text);
        Assert.Equal("/section/wiki-page", model.Nav[1].Href);
    }

    [Fact]
    public void Show_HttpGetAttribute_HasOrder_EqualTo_IntMaxValue()
    {
        var method = typeof(PageController).GetMethod(nameof(PageController.Show))!;
        var attr = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(int.MaxValue, attr!.Order);
    }
}
