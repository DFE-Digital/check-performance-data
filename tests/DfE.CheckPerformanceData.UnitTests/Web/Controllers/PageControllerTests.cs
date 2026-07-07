using System.Reflection;
using System.Security.Claims;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Verifies the catch-all page resolver:
//   - unknown path → 404 (GetNodeByPathAsync returns null)
//   - content/wiki with no live version + non-editor → 404
//   - content/wiki with no live version + editor → View with IsPreview=true
//   - content node → View("Content", RenderedPageViewModel) with deserialised tree + auto nav
//   - wiki node   → View("Wiki",    RenderedPageViewModel) with raw HTML
//   - folder node → View("Folder",  RenderedPageViewModel) with Nav = ordered children
//   - [HttpGet("/{*path}", Order = int.MaxValue)] enforced via reflection
public sealed class PageControllerTests
{
    private readonly IPageNodeService _pageNodes = Substitute.For<IPageNodeService>();

    // Anonymous user (no roles) — exercises the "not an editor" path.
    private PageController CreateSut() => CreateSutWithRole(null);

    private PageController CreateSutWithRole(string? role)
    {
        var sut = new PageController(_pageNodes);
        var claims = new List<Claim>();
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));
        var identity = new ClaimsIdentity(claims, role is not null ? "test" : null);
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return sut;
    }

    private static PageNodeDto Node(
        string pageType, string title = "Test Page", string path = "test",
        Guid? id = null, Guid? parentId = null) => new()
    {
        Id       = id ?? Guid.Empty,
        ParentId = parentId,
        Segment  = path.Split('/').Last(),
        Path     = path,
        Title    = title,
        PageType = pageType
    };

    private static LivePageResult Live(string pageType, string content, string title = "Test Page") => new()
    {
        Node    = Node(pageType, title),
        Version = new PageNodeVersionDto { Content = content }
    };

    [Fact]
    public async Task Show_ReturnsNotFound_WhenPathDoesNotExist()
    {
        _pageNodes.GetNodeByPathAsync("unknown").Returns((PageNodeDto?)null);

        var result = await CreateSut().Show("unknown");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsNotFound_WhenNodeHasNoLiveVersion_AndUserIsNotEditor()
    {
        // Node exists (content type) but no version is currently live; anonymous user → 404.
        _pageNodes.GetNodeByPathAsync("unpublished")
            .Returns(Node("content", "Unpublished", "unpublished"));
        _pageNodes.GetLivePageAsync("unpublished", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);

        var result = await CreateSut().Show("unpublished");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Show_ReturnsContentPreview_WhenNoLiveVersion_AndUserIsEditor()
    {
        const string draftJson = """[{"kind":"widget","type":"heading","anchor":"h1","props":{"level":2,"text":"Draft"}}]""";
        var nodeId = Guid.NewGuid();
        _pageNodes.GetNodeByPathAsync("draft-content")
            .Returns(Node("content", "Draft Page", "draft-content", id: nodeId));
        _pageNodes.GetLivePageAsync("draft-content", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);
        _pageNodes.GetWorkingOrLatestContentAsync(nodeId)
            .Returns(draftJson);

        var result = await CreateSutWithRole(WikiConstants.EditorRole).Show("draft-content");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Content", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.True(model.IsPreview);
        Assert.Equal("Draft Page", model.Title);
        Assert.NotNull(model.Content);
        await _pageNodes.Received(1).GetWorkingOrLatestContentAsync(nodeId);
    }

    [Fact]
    public async Task Show_ReturnsWikiPreview_WhenNoLiveVersion_AndUserIsEditor()
    {
        var nodeId   = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var wikiNode = new PageNodeDto
        {
            Id       = nodeId,
            ParentId = parentId,
            Segment  = "draft-wiki",
            Path     = "draft-wiki",
            Title    = "Draft Wiki",
            PageType = "wiki"
        };
        _pageNodes.GetNodeByPathAsync("draft-wiki").Returns(wikiNode);
        _pageNodes.GetLivePageAsync("draft-wiki", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);
        _pageNodes.GetWorkingOrLatestContentAsync(nodeId)
            .Returns("<p>Draft body</p>");
        _pageNodes.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());

        var result = await CreateSutWithRole(WikiConstants.EditorRole).Show("draft-wiki");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Wiki", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.True(model.IsPreview);
        Assert.Equal("Draft Wiki", model.Title);
        Assert.Equal("<p>Draft body</p>", model.WikiHtml);
        await _pageNodes.Received(1).GetWorkingOrLatestContentAsync(nodeId);
    }

    [Fact]
    public async Task Show_ReturnsNotFound_ForWikiWithNoLiveVersion_WhenUserIsNotEditor()
    {
        _pageNodes.GetNodeByPathAsync("draft-wiki")
            .Returns(Node("wiki", "Draft Wiki", "draft-wiki"));
        _pageNodes.GetLivePageAsync("draft-wiki", Arg.Any<DateTime>())
            .Returns((LivePageResult?)null);

        var result = await CreateSut().Show("draft-wiki");

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
        _pageNodes.GetNodeByPathAsync("my-page")
            .Returns(Node("content", "My Page", "my-page"));
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
        _pageNodes.GetNodeByPathAsync("wiki-page")
            .Returns(Node("wiki", "Wiki Page", "wiki-page"));
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
    public async Task Show_ReturnsContentView_WithIsPreviewFalse_WhenPageIsLive()
    {
        const string json = """[{"kind":"widget","type":"heading","anchor":"a","props":{"level":2,"text":"A"}}]""";
        _pageNodes.GetNodeByPathAsync("live-content")
            .Returns(Node("content", "Live Content", "live-content"));
        _pageNodes.GetLivePageAsync("live-content", Arg.Any<DateTime>())
            .Returns(Live("content", json, "Live Content"));

        // Even an editor viewing a published page should get IsPreview=false.
        var result = await CreateSutWithRole(WikiConstants.EditorRole).Show("live-content");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.False(model.IsPreview);
    }

    [Fact]
    public async Task Show_ReturnsWikiView_WithIsPreviewFalse_WhenPageIsLive()
    {
        _pageNodes.GetNodeByPathAsync("live-wiki")
            .Returns(Node("wiki", "Live Wiki", "live-wiki"));
        _pageNodes.GetLivePageAsync("live-wiki", Arg.Any<DateTime>())
            .Returns(Live("wiki", "<p>Published</p>", "Live Wiki"));

        var result = await CreateSutWithRole(WikiConstants.EditorRole).Show("live-wiki");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.False(model.IsPreview);
    }

    [Fact]
    public async Task Show_ReturnsFolderView_WithChildNav_OrderedBySortOrder()
    {
        var folderId = Guid.NewGuid();
        _pageNodes.GetNodeByPathAsync("support")
            .Returns(Node("folder", "Support", "support", id: folderId));
        _pageNodes.GetTreeAsync()
            .Returns(new List<PageNodeTreeItemDto>
            {
                // intentionally out of SortOrder to verify ordering
                new() { Id = Guid.NewGuid(), ParentId = folderId, Segment = "contact", Path = "support/contact", SortOrder = 1, Title = "Contact", PageType = "content" },
                new() { Id = Guid.NewGuid(), ParentId = folderId, Segment = "faq",     Path = "support/faq",     SortOrder = 0, Title = "FAQ",     PageType = "content" },
                // unrelated root node — must be excluded
                new() { Id = Guid.NewGuid(), ParentId = null,     Segment = "other",   Path = "other",           SortOrder = 0, Title = "Other",   PageType = "content" }
            });

        var result = await CreateSut().Show("support");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Folder", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("Support", model.Title);
        Assert.Equal("folder", model.PageType);
        Assert.NotNull(model.Nav);
        Assert.Equal(2, model.Nav!.Count);
        // FAQ first (SortOrder 0), Contact second (SortOrder 1)
        Assert.Equal("FAQ",     model.Nav[0].Text);
        Assert.Equal("/support/faq",     model.Nav[0].Href);
        Assert.Equal("Contact", model.Nav[1].Text);
        Assert.Equal("/support/contact", model.Nav[1].Href);
    }

    [Fact]
    public async Task Show_ReturnsFolderView_WithEmptyNav_WhenNoChildren()
    {
        var folderId = Guid.NewGuid();
        _pageNodes.GetNodeByPathAsync("empty-folder")
            .Returns(Node("folder", "Empty Folder", "empty-folder", id: folderId));
        _pageNodes.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());

        var result = await CreateSut().Show("empty-folder");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Folder", view.ViewName);
        var model = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.NotNull(model.Nav);
        Assert.Empty(model.Nav!);
    }

    [Fact]
    public async Task Show_ReturnsWikiView_WithSiblingNavOrderedBySortOrder()
    {
        var parentId = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();

        var wikiNode = new PageNodeDto
        {
            Id       = nodeId,
            ParentId = parentId,
            Segment  = "wiki-page",
            Path     = "section/wiki-page",
            Title    = "Wiki Page",
            PageType = "wiki"
        };

        _pageNodes.GetNodeByPathAsync("section/wiki-page").Returns(wikiNode);
        _pageNodes.GetLivePageAsync("section/wiki-page", Arg.Any<DateTime>())
            .Returns(new LivePageResult
            {
                Node    = wikiNode,
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
        Assert.Equal("First",              model.Nav[0].Text);
        Assert.Equal("/section/first",     model.Nav[0].Href);
        Assert.Equal("Wiki Page",          model.Nav[1].Text);
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
