using System.Reflection;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Pins the controller's thin wiring: GetTreeAsync rows are handed to PageTreeBuilder, the
// resulting tree is passed as view model, and the security contract (editor-role gate) holds.
// Also covers GET /admin/pages/new and POST /admin/pages/create.
public sealed class PageTreeAdminControllerTests
{
    private readonly IPageNodeService _service = Substitute.For<IPageNodeService>();

    private PageTreeAdminController Sut(PageNodePathValidator? validator = null)
    {
        var controller = new PageTreeAdminController(_service, validator ?? OpenValidator());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    // Returns a validator that accepts any path with valid chars and no reserved routes.
    private static PageNodePathValidator OpenValidator(params string[] reservedSegments)
    {
        var provider = Substitute.For<IReservedRouteProvider>();
        provider.ReservedFirstSegments().Returns(
            new HashSet<string>(reservedSegments, StringComparer.OrdinalIgnoreCase));
        return new PageNodePathValidator(provider);
    }

    // ── Index ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsView_WithBuiltTree()
    {
        var rootId = Guid.NewGuid();
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = rootId, Segment = "home", Path = "home", Title = "Home", PageType = "folder", SortOrder = 1 }
        });

        var result = await Sut().Index();

        var view  = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<PageTreeNode>>(view.Model);
        Assert.Single(model);
        Assert.Equal("Home", model[0].Title);
        Assert.Equal("home", model[0].Path);
    }

    [Fact]
    public async Task Index_BuildsNestedTree_WhenChildRowsProvided()
    {
        var parentId = Guid.NewGuid();
        var childId  = Guid.NewGuid();

        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new()
            {
                Id       = parentId,
                Segment  = "parent",
                Path     = "parent",
                Title    = "Parent",
                PageType = "folder",
                SortOrder = 1
            },
            new()
            {
                Id            = childId,
                ParentId      = parentId,
                Segment       = "child",
                Path          = "parent/child",
                Title         = "Child",
                PageType      = "content",
                SortOrder     = 1,
                HasLiveVersion = true
            }
        });

        var result = await Sut().Index();

        var view  = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<PageTreeNode>>(view.Model);
        Assert.Single(model);                        // one root
        Assert.Single(model[0].Children);            // one nested child
        Assert.Equal("Child", model[0].Children[0].Title);
        Assert.True(model[0].Children[0].HasLiveVersion);
    }

    [Fact]
    public async Task Index_ReturnsEmptyList_WhenNoRows()
    {
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());

        var result = await Sut().Index();

        var view  = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<PageTreeNode>>(view.Model);
        Assert.Empty(model);
    }

    // ── GET /admin/pages/new ─────────────────────────────────────────────────

    [Fact]
    public async Task New_WithNoParentId_ReturnsView_WithNullParentTitle()
    {
        var result = await Sut().New(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<NewPageViewModel>(view.Model);
        Assert.Null(model.ParentId);
        Assert.Null(model.ParentTitle);
    }

    [Fact]
    public async Task New_WithValidParentId_ReturnsView_WithParentTitle()
    {
        var parentId = Guid.NewGuid();
        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "section", Path = "section",
            Title = "Section", PageType = "folder"
        });

        var result = await Sut().New(parentId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<NewPageViewModel>(view.Model);
        Assert.Equal(parentId, model.ParentId);
        Assert.Equal("Section", model.ParentTitle);
    }

    [Fact]
    public async Task New_WithUnknownParentId_ReturnsNotFound()
    {
        var badId = Guid.NewGuid();
        _service.GetNodeByIdAsync(badId).Returns((PageNodeDto?)null);

        var result = await Sut().New(badId);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── POST /admin/pages/create ─────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidContent_RedirectsToEditUrl()
    {
        var nodeId = Guid.NewGuid();
        _service.GetNodeByPathAsync("my-page").Returns((PageNodeDto?)null);
        _service.CreatePageAsync(null, "my-page", "My Page", "content", Arg.Any<string?>())
            .Returns(new PageNodeDto
            {
                Id = nodeId, Segment = "my-page", Path = "my-page",
                Title = "My Page", PageType = "content"
            });

        var result = await Sut().Create(null, "content", "my-page", "My Page");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{nodeId}/edit", redirect.Url);
    }

    [Fact]
    public async Task Create_ValidWiki_RedirectsToEditUrl()
    {
        var nodeId = Guid.NewGuid();
        _service.GetNodeByPathAsync("my-wiki").Returns((PageNodeDto?)null);
        _service.CreatePageAsync(null, "my-wiki", "My Wiki", "wiki", Arg.Any<string?>())
            .Returns(new PageNodeDto
            {
                Id = nodeId, Segment = "my-wiki", Path = "my-wiki",
                Title = "My Wiki", PageType = "wiki"
            });

        var result = await Sut().Create(null, "wiki", "my-wiki", "My Wiki");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{nodeId}/edit", redirect.Url);
    }

    [Fact]
    public async Task Create_ValidFolder_RedirectsToTree()
    {
        var nodeId = Guid.NewGuid();
        _service.GetNodeByPathAsync("my-folder").Returns((PageNodeDto?)null);
        _service.CreatePageAsync(null, "my-folder", "My Folder", "folder", Arg.Any<string?>())
            .Returns(new PageNodeDto
            {
                Id = nodeId, Segment = "my-folder", Path = "my-folder",
                Title = "My Folder", PageType = "folder"
            });

        var result = await Sut().Create(null, "folder", "my-folder", "My Folder");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/pages", redirect.Url);
    }

    [Fact]
    public async Task Create_InvalidPath_ReturnsFormWithError_AndDoesNotCreate()
    {
        // Segment with spaces fails the path regex — no reserved-routes setup needed.
        var result = await Sut().Create(null, "content", "Bad Segment", "A Title");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<NewPageViewModel>(view.Model);
        Assert.NotNull(model.Error);
        await _service.DidNotReceive().CreatePageAsync(
            Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Create_DuplicatePath_ReturnsFormWithError_AndDoesNotCreate()
    {
        var existingId = Guid.NewGuid();
        _service.GetNodeByPathAsync("existing").Returns(new PageNodeDto
        {
            Id = existingId, Segment = "existing", Path = "existing",
            Title = "Existing", PageType = "content"
        });

        var result = await Sut().Create(null, "folder", "existing", "Existing");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<NewPageViewModel>(view.Model);
        Assert.Contains("already exists", model.Error, StringComparison.OrdinalIgnoreCase);
        await _service.DidNotReceive().CreatePageAsync(
            Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Create_InvalidPageType_ReturnsFormWithError_AndDoesNotCreate()
    {
        var result = await Sut().Create(null, "unknown-type", "my-page", "My Page");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<NewPageViewModel>(view.Model);
        Assert.NotNull(model.Error);
        await _service.DidNotReceive().CreatePageAsync(
            Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Create_WithUnknownParentId_ReturnsNotFound()
    {
        var badId = Guid.NewGuid();
        _service.GetNodeByIdAsync(badId).Returns((PageNodeDto?)null);

        var result = await Sut().Create(badId, "content", "child-page", "Child Page");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Attribute / security contracts ──────────────────────────────────────

    [Fact]
    public void Controller_IsGatedToTheEditorRole()
    {
        var authorize = typeof(PageTreeAdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(WikiConstants.EditorRole, authorize!.Roles);
    }

    [Fact]
    public void Create_Has_ValidateAntiForgeryToken()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Create));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
