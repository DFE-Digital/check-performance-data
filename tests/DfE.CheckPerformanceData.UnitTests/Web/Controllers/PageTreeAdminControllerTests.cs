using System.Reflection;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Pins the controller's thin wiring: GetTreeAsync rows are handed to PageTreeBuilder, the
// resulting tree is passed as view model, and the security contract (editor-role gate) holds.
// Also covers GET /admin/pages/new, POST /admin/pages/create, version history, publish,
// wiki edit/save, and content widget-editor (GET + mutation POSTs).
public sealed class PageTreeAdminControllerTests
{
    private readonly IPageNodeService _service = Substitute.For<IPageNodeService>();
    private readonly IHtmlRenderingService _renderer = Substitute.For<IHtmlRenderingService>();
    private readonly IPageNodeContentEditor _contentEditor = Substitute.For<IPageNodeContentEditor>();

    private PageTreeAdminController Sut(PageNodePathValidator? validator = null)
    {
        var controller = new PageTreeAdminController(
            _service, validator ?? OpenValidator(), _renderer, _contentEditor);
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
    public async Task Create_ValidContentUnderParent_ComputesCompositePath_AndRedirectsToEdit()
    {
        var parentId = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "section", Path = "section",
            Title = "Section", PageType = "folder"
        });
        _service.GetNodeByPathAsync("section/child-page").Returns((PageNodeDto?)null);
        _service.CreatePageAsync(parentId, "child-page", "Child Page", "content", Arg.Any<string?>())
            .Returns(new PageNodeDto
            {
                Id = nodeId, Segment = "child-page", Path = "section/child-page",
                Title = "Child Page", PageType = "content"
            });

        var result = await Sut().Create(parentId, "content", "child-page", "Child Page");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{nodeId}/edit", redirect.Url);
        await _service.Received(1).GetNodeByPathAsync("section/child-page");
        await _service.Received(1).CreatePageAsync(
            parentId, "child-page", "Child Page", "content", Arg.Any<string?>());
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

    // ── GET /admin/pages/{id}/versions ───────────────────────────────────────

    [Fact]
    public async Task Versions_UnknownId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().Versions(id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Versions_KnownId_ReturnsViewWithVersions()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "my-page", Path = "my-page",
            Title = "My Page", PageType = "wiki"
        });
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 2, IsCurrent = true, Content = "<p>v2</p>" },
            new() { Id = Guid.NewGuid(), VersionId = 1, IsCurrent = false, Content = "<p>v1</p>" }
        });

        var result = await Sut().Versions(id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Versions", view.ViewName);
        var vm = Assert.IsType<PageTreeAdminVersionsViewModel>(view.Model);
        Assert.Equal(id, vm.NodeId);
        Assert.Equal("My Page", vm.NodeTitle);
        Assert.Equal(2, vm.Versions.Count);
    }

    // ── POST /admin/pages/{id}/publish ───────────────────────────────────────

    [Fact]
    public async Task Publish_ValidPost_CallsPublishAsyncAndRedirects()
    {
        var id = Guid.NewGuid();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await Sut().Publish(id, 3, from, null);

        await _service.Received(1).PublishAsync(id, 3, from, null, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/versions", redirect.Url);
    }

    [Fact]
    public void Publish_Action_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Publish));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── GET /admin/pages/{id}/edit ───────────────────────────────────────────

    [Fact]
    public async Task Edit_UnknownId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().Edit(id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_WikiNode_ReturnsWikiEditView()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki-page", Path = "wiki-page",
            Title = "Wiki Page", PageType = "wiki"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("<p>Hello</p>");

        var result = await Sut().Edit(id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("WikiEdit", view.ViewName);
        var vm = Assert.IsType<PageTreeAdminWikiEditViewModel>(view.Model);
        Assert.Equal(id, vm.NodeId);
        Assert.Equal("Wiki Page", vm.NodeTitle);
        Assert.Equal("<p>Hello</p>", vm.Content);
    }

    [Fact]
    public async Task Edit_WikiNode_AfterPublish_ShowsPublishedContent()
    {
        // After publish there is no draft; GetWorkingOrLatestContentAsync returns the
        // published version's content so the editor starts from current state, not blank.
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki-page", Path = "wiki-page",
            Title = "Wiki Page", PageType = "wiki"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("<p>Published content</p>");

        var result = await Sut().Edit(id);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PageTreeAdminWikiEditViewModel>(view.Model);
        Assert.Equal("<p>Published content</p>", vm.Content);
    }

    [Fact]
    public async Task Edit_ContentNode_ReturnsContentEditView_WithCorrectActionBaseAndTree()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "content-page", Path = "content-page",
            Title = "My Content Page", PageType = "content"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns(
            """[{"kind":"widget","type":"divider"}]""");

        var result = await Sut().Edit(id);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ContentPageEditViewModel>(view.Model);
        Assert.Equal($"/admin/pages/{id}/content", vm.ActionBase);
        Assert.False(vm.ShowInlinePublish);
        Assert.Single(vm.Content);
    }

    [Fact]
    public async Task Edit_ContentNode_NullContent_DeserializesEmptyTree()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "content-page", Path = "content-page",
            Title = "Empty Page", PageType = "content"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns((string?)null);

        var result = await Sut().Edit(id);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ContentPageEditViewModel>(view.Model);
        Assert.Empty(vm.Content);
    }

    [Fact]
    public async Task Edit_FolderNode_RedirectsToAdminPages()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "section", Path = "section",
            Title = "Section", PageType = "folder"
        });

        var result = await Sut().Edit(id);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/pages", redirect.Url);
    }

    // ── POST /admin/pages/{id}/save ──────────────────────────────────────────

    [Fact]
    public async Task Save_ValidPost_CallsSaveWorkingContentAndRedirects()
    {
        var id = Guid.NewGuid();
        const string rawHtml   = "<p>Hello <script>x</script>world</p>";
        const string sanitised = "<p>Hello world</p>";   // distinct from rawHtml
        _renderer.RenderHtml(rawHtml).Returns(sanitised);
        _renderer.StripTagsToPlainText(sanitised).Returns("Hello world");
        // StripTagsToPlainText(rawHtml) is intentionally NOT stubbed: if the controller
        // wrongly passed raw content instead of the sanitised output, NSubstitute would
        // return null and the SaveWorkingContentAsync assertion below would fail.

        var result = await Sut().Save(id, rawHtml);

        await _service.Received(1).SaveWorkingContentAsync(id, rawHtml, "Hello world", Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public void Save_Action_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Save));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/content/add ───────────────────────────────────

    [Fact]
    public async Task ContentAdd_Widget_CallsAddWidgetAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentAdd(id, "0.0", widgetType: "heading", regionLayout: null);

        await _contentEditor.Received(1).AddWidgetAsync(
            id,
            Arg.Is<IReadOnlyList<TreeStep>>(p => p.SequenceEqual(new[] { new TreeStep(0, 0) })),
            "heading",
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public async Task ContentAdd_Region_CallsAddRegionAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentAdd(id, "0.1", widgetType: null, regionLayout: "Halves");

        await _contentEditor.Received(1).AddRegionAsync(
            id,
            Arg.Is<IReadOnlyList<TreeStep>>(p => p.SequenceEqual(new[] { new TreeStep(0, 1) })),
            RegionLayout.Halves,
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    // ── POST /admin/pages/{id}/content/move ──────────────────────────────────

    [Fact]
    public async Task ContentMove_Up_CallsMoveUpAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentMove(id, "0.2", "up");

        await _contentEditor.Received(1).MoveUpAsync(
            id,
            Arg.Is<IReadOnlyList<TreeStep>>(p => p.SequenceEqual(new[] { new TreeStep(0, 2) })),
            Arg.Any<string?>());
        Assert.Equal($"/admin/pages/{id}/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task ContentMove_Down_CallsMoveDownAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentMove(id, "0.0", "down");

        await _contentEditor.Received(1).MoveDownAsync(
            id,
            Arg.Any<IReadOnlyList<TreeStep>>(),
            Arg.Any<string?>());
        Assert.Equal($"/admin/pages/{id}/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    // ── POST /admin/pages/{id}/content/delete ────────────────────────────────

    [Fact]
    public async Task ContentDelete_CallsDeleteAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentDelete(id, "0.0");

        await _contentEditor.Received(1).DeleteAsync(
            id,
            Arg.Any<IReadOnlyList<TreeStep>>(),
            Arg.Any<string?>());
        Assert.Equal($"/admin/pages/{id}/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    // ── POST /admin/pages/{id}/content/widget ────────────────────────────────

    [Fact]
    public async Task ContentWidget_BuildsPropsAndCallsUpdateWidgetAsync_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentWidget(
            id, "0.0", "heading",
            new Dictionary<string, string?> { ["level"] = "2", ["text"] = "KS4 dates" });

        await _contentEditor.Received(1).UpdateWidgetAsync(
            id,
            Arg.Any<IReadOnlyList<TreeStep>>(),
            Arg.Is<System.Text.Json.Nodes.JsonObject>(p => (string)p["text"]! == "KS4 dates"),
            Arg.Any<string?>());
        Assert.Equal($"/admin/pages/{id}/edit", Assert.IsType<RedirectResult>(result).Url);
    }

    // ── GET /admin/pages/{id}/delete ─────────────────────────────────────────

    [Fact]
    public async Task DeleteConfirm_UnknownId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().DeleteConfirm(id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteConfirm_LeafNode_ReturnsDeleteView_WithoutChildrenBlock()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "leaf", Path = "leaf", Title = "Leaf Page", PageType = "content"
        });
        // No children in the tree
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = id, Segment = "leaf", Path = "leaf", Title = "Leaf Page", PageType = "content", SortOrder = 1 }
        });

        var result = await Sut().DeleteConfirm(id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Delete", view.ViewName);
        var vm = Assert.IsType<PageNodeDeleteViewModel>(view.Model);
        Assert.Equal(id, vm.Id);
        Assert.Equal("Leaf Page", vm.Title);
        Assert.False(vm.HasChildren);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task DeleteConfirm_NodeWithChildren_ReturnsDeleteView_WithChildrenBlock()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "parent", Path = "parent", Title = "Parent", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = parentId, Segment = "parent", Path = "parent", Title = "Parent", PageType = "folder", SortOrder = 1 },
            new() { Id = childId, ParentId = parentId, Segment = "child", Path = "parent/child", Title = "Child", PageType = "content", SortOrder = 1 }
        });

        var result = await Sut().DeleteConfirm(parentId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PageNodeDeleteViewModel>(view.Model);
        Assert.True(vm.HasChildren);
    }

    // ── POST /admin/pages/{id}/delete ────────────────────────────────────────

    [Fact]
    public async Task DeletePost_UnknownId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().DeletePost(id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeletePost_Success_CallsDeleteAsync_AndRedirectsToTree()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "leaf", Path = "leaf", Title = "Leaf", PageType = "content"
        });
        _service.DeleteAsync(id, Arg.Any<string?>()).Returns(Task.CompletedTask);

        var result = await Sut().DeletePost(id);

        await _service.Received(1).DeleteAsync(id, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/pages", redirect.Url);
    }

    [Fact]
    public async Task DeletePost_WithChildren_CatchesException_RerendersDeleteView_WithError()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "parent", Path = "parent", Title = "Parent", PageType = "folder"
        });
        _service.DeleteAsync(id, Arg.Any<string?>())
            .Returns(Task.FromException(new InvalidOperationException("has children")));
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = id, Segment = "parent", Path = "parent", Title = "Parent", PageType = "folder", SortOrder = 1 },
            new() { Id = Guid.NewGuid(), ParentId = id, Segment = "child", Path = "parent/child", Title = "Child", PageType = "content", SortOrder = 1 }
        });

        var result = await Sut().DeletePost(id);

        // Must NOT redirect to success — must re-render the confirm page with error
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Delete", view.ViewName);
        var vm = Assert.IsType<PageNodeDeleteViewModel>(view.Model);
        Assert.NotNull(vm.Error);
        Assert.True(vm.HasChildren);
    }

    [Fact]
    public void DeletePost_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.DeletePost));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/move ───────────────────────────────────────────

    [Fact]
    public async Task Move_UnknownId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().Move(id, "up");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Move_InvalidDirection_ReturnsBadRequest_DoesNotCallService()
    {
        var id = Guid.NewGuid();

        var result = await Sut().Move(id, "sideways");

        Assert.IsType<BadRequestResult>(result);
        await _service.DidNotReceive().MoveAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Move_KnownId_Up_CallsMoveAsyncAndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.MoveAsync(id, "up").Returns(Task.CompletedTask);

        var result = await Sut().Move(id, "up");

        await _service.Received(1).MoveAsync(id, "up");
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/pages", redirect.Url);
    }

    [Fact]
    public async Task Move_KnownId_Down_CallsMoveAsyncWithDownAndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.MoveAsync(id, "down").Returns(Task.CompletedTask);

        var result = await Sut().Move(id, "down");

        await _service.Received(1).MoveAsync(id, "down");
        Assert.Equal("/admin/pages", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public void Move_Action_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Move));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── M-01: content-mutation unknown-node guard ─────────────────────────────

    [Fact]
    public async Task ContentAdd_UnknownNode_ReturnsNotFound_DoesNotCallEditor()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentAdd(id, "0.0", "heading", null);

        Assert.IsType<NotFoundResult>(result);
        await _contentEditor.DidNotReceive().AddWidgetAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<TreeStep>>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ContentMove_UnknownNode_ReturnsNotFound_DoesNotCallEditor()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentMove(id, "0.0", "up");

        Assert.IsType<NotFoundResult>(result);
        await _contentEditor.DidNotReceive().MoveUpAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<TreeStep>>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ContentDelete_UnknownNode_ReturnsNotFound_DoesNotCallEditor()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentDelete(id, "0.0");

        Assert.IsType<NotFoundResult>(result);
        await _contentEditor.DidNotReceive().DeleteAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<TreeStep>>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ContentWidget_UnknownNode_ReturnsNotFound_DoesNotCallEditor()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentWidget(id, "0.0", "heading", new Dictionary<string, string?>());

        Assert.IsType<NotFoundResult>(result);
        await _contentEditor.DidNotReceive().UpdateWidgetAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<TreeStep>>(), Arg.Any<System.Text.Json.Nodes.JsonObject>(), Arg.Any<string?>());
    }

    // ── Antiforgery contract ─────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(PageTreeAdminController.ContentAdd))]
    [InlineData(nameof(PageTreeAdminController.ContentMove))]
    [InlineData(nameof(PageTreeAdminController.ContentDelete))]
    [InlineData(nameof(PageTreeAdminController.ContentWidget))]
    public void ContentMutationPosts_HaveValidateAntiForgeryTokenAndHttpPost(string actionName)
    {
        var method = typeof(PageTreeAdminController).GetMethod(actionName)!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    // ── Route non-collision ───────────────────────────────────────────────────

    [Fact]
    public void ContentDelete_RouteTemplate_ContainsContentSlash_NotJustDelete()
    {
        // Widget-delete sits under /content/delete so it cannot collide with the
        // node-level page-delete at /admin/pages/{id}/delete.
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.ContentDelete))!;
        var httpPost = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Contains("/content/delete", httpPost!.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{id:guid}/delete\"", httpPost.Template + "\"", StringComparison.OrdinalIgnoreCase);
    }
}
