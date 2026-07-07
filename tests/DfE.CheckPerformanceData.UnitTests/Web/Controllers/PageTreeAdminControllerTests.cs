using System.Reflection;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
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
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly SamplePageNodeSeeder _sampleSeeder = new(Substitute.For<IPageNodeService>());

    private PageTreeAdminController Sut(PageNodePathValidator? validator = null)
    {
        var controller = new PageTreeAdminController(
            _service, validator ?? OpenValidator(), _renderer, _contentEditor, _settings, _sampleSeeder);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.ControllerContext.HttpContext,
            Substitute.For<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
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

    // ── Index (grid) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_Root_ReturnsView_WithRootChildren_AndAllPagesTitle()
    {
        var rootId1 = Guid.NewGuid();
        var rootId2 = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = rootId1, Segment = "home",   Path = "home",   Title = "Home",    PageType = "folder",  SortOrder = 1 },
            new() { Id = rootId2, Segment = "help",   Path = "help",   Title = "Help",    PageType = "wiki",    SortOrder = 2 },
            // child of rootId1 — must NOT appear at root level
            new() { Id = childId, ParentId = rootId1, Segment = "sub", Path = "home/sub", Title = "Sub", PageType = "content", SortOrder = 1 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<PageTreeGridViewModel>(view.Model);
        Assert.Equal("All pages", vm.SelectedTitle);
        Assert.Null(vm.SelectedId);
        // Only the two root nodes; the deeper child must be excluded.
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.Children.Count);
        Assert.Contains(vm.Children, r => r.Title == "Home");
        Assert.Contains(vm.Children, r => r.Title == "Help");
        Assert.DoesNotContain(vm.Children, r => r.Title == "Sub");
    }

    [Fact]
    public async Task Index_WithId_ReturnsView_WithDirectChildrenOnly()
    {
        var parentId = Guid.NewGuid();
        var childId1 = Guid.NewGuid();
        var childId2 = Guid.NewGuid();
        var grandId  = Guid.NewGuid();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "section", Path = "section", Title = "Section", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = parentId, Segment = "section",  Path = "section",        Title = "Section",  PageType = "folder",  SortOrder = 1 },
            new() { Id = childId1, ParentId = parentId,  Segment = "page-a", Path = "section/page-a", Title = "Page A",   PageType = "content", SortOrder = 1 },
            new() { Id = childId2, ParentId = parentId,  Segment = "page-b", Path = "section/page-b", Title = "Page B",   PageType = "wiki",    SortOrder = 2 },
            // grandchild of parentId — must NOT appear in the direct-children listing
            new() { Id = grandId,  ParentId = childId1,  Segment = "grand",  Path = "section/page-a/grand", Title = "Grand", PageType = "content", SortOrder = 1 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(parentId, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<PageTreeGridViewModel>(view.Model);
        Assert.Equal(parentId, vm.SelectedId);
        Assert.Equal("Section", vm.SelectedTitle);
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.Children.Count);
        Assert.Contains(vm.Children, r => r.Title == "Page A");
        Assert.Contains(vm.Children, r => r.Title == "Page B");
        Assert.DoesNotContain(vm.Children, r => r.Title == "Grand");
    }

    [Fact]
    public async Task Index_WithUnknownId_ReturnsNotFound()
    {
        var badId = Guid.NewGuid();
        _service.GetNodeByIdAsync(badId).Returns((PageNodeDto?)null);

        var result = await Sut().Index(badId, null, 1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Index_Search_FiltersChildren_ByTitleAndSegment_CaseInsensitive()
    {
        var parentId = Guid.NewGuid();
        var matchId1 = Guid.NewGuid();
        var matchId2 = Guid.NewGuid();
        var noMatchId = Guid.NewGuid();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "parent", Path = "parent", Title = "Parent", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = parentId,  Segment = "parent",         Path = "parent",              Title = "Parent",         PageType = "folder",  SortOrder = 0 },
            // matches by Title (case-insensitive "ks4")
            new() { Id = matchId1,  ParentId = parentId, Segment = "ks4-dates",  Path = "parent/ks4-dates",  Title = "KS4 Dates",  PageType = "content", SortOrder = 1 },
            // matches by Segment ("check")
            new() { Id = matchId2,  ParentId = parentId, Segment = "check-data", Path = "parent/check-data", Title = "Data check", PageType = "content", SortOrder = 2 },
            // does NOT match
            new() { Id = noMatchId, ParentId = parentId, Segment = "about",      Path = "parent/about",      Title = "About us",   PageType = "wiki",    SortOrder = 3 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(parentId, "ks4", 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<PageTreeGridViewModel>(view.Model);
        Assert.Equal("ks4", vm.SearchQuery);
        Assert.Equal(1, vm.TotalCount);
        Assert.Single(vm.Children);
        Assert.Equal("KS4 Dates", vm.Children[0].Title);

        // Search on segment — "check" hits the second row
        var result2 = await Sut().Index(parentId, "CHECK", 1);
        var vm2 = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result2).Model);
        Assert.Equal(1, vm2.TotalCount);
        Assert.Equal("Data check", vm2.Children[0].Title);
    }

    [Fact]
    public async Task Index_Paging_Page1_ReturnsFirstItems_AndComputesTotalPages()
    {
        var parentId = Guid.NewGuid();
        var ids = Enumerable.Range(1, 5).Select(_ => Guid.NewGuid()).ToList();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "root", Path = "root", Title = "Root", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(ids.Select((id, i) => new PageNodeTreeItemDto
        {
            Id = id, ParentId = parentId, Segment = $"page-{i + 1}", Path = $"root/page-{i + 1}",
            Title = $"Page {i + 1}", PageType = "content", SortOrder = i + 1
        }).ToList());
        // Stub page size = 2
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(2);

        var result = await Sut().Index(parentId, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(2, vm.PageSize);
        Assert.Equal(3, vm.TotalPages);  // ceil(5/2)
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(2, vm.Children.Count);
        Assert.Equal("Page 1", vm.Children[0].Title);
        Assert.Equal("Page 2", vm.Children[1].Title);
    }

    [Fact]
    public async Task Index_Paging_Page2_ReturnsNextItems()
    {
        var parentId = Guid.NewGuid();
        var ids = Enumerable.Range(1, 5).Select(_ => Guid.NewGuid()).ToList();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "root", Path = "root", Title = "Root", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(ids.Select((id, i) => new PageNodeTreeItemDto
        {
            Id = id, ParentId = parentId, Segment = $"page-{i + 1}", Path = $"root/page-{i + 1}",
            Title = $"Page {i + 1}", PageType = "content", SortOrder = i + 1
        }).ToList());
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(2);

        var result = await Sut().Index(parentId, null, 2);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, vm.CurrentPage);
        Assert.Equal(2, vm.Children.Count);
        Assert.Equal("Page 3", vm.Children[0].Title);
        Assert.Equal("Page 4", vm.Children[1].Title);
    }

    [Fact]
    public async Task Index_Paging_NegativeOrZeroPage_ClampsToOne()
    {
        var parentId = Guid.NewGuid();
        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "root", Path = "root", Title = "Root", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = Guid.NewGuid(), ParentId = parentId, Segment = "a", Path = "root/a", Title = "A", PageType = "content", SortOrder = 1 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(parentId, null, -5);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task Index_ChildRows_CarryHasLiveVersion()
    {
        var parentId = Guid.NewGuid();
        var liveId   = Guid.NewGuid();
        var draftId  = Guid.NewGuid();

        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "root", Path = "root", Title = "Root", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = liveId,  ParentId = parentId, Segment = "live",  Path = "root/live",  Title = "Live",  PageType = "content", SortOrder = 1, HasLiveVersion = true },
            new() { Id = draftId, ParentId = parentId, Segment = "draft", Path = "root/draft", Title = "Draft", PageType = "content", SortOrder = 2, HasLiveVersion = false }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(parentId, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        var live  = Assert.Single(vm.Children, r => r.Title == "Live");
        var draft = Assert.Single(vm.Children, r => r.Title == "Draft");
        Assert.True(live.HasLiveVersion);
        Assert.False(draft.HasLiveVersion);
    }

    [Fact]
    public async Task Index_FallsBackToDefaultPageSize_WhenSettingReturnsZero()
    {
        var parentId = Guid.NewGuid();
        _service.GetNodeByIdAsync(parentId).Returns(new PageNodeDto
        {
            Id = parentId, Segment = "root", Path = "root", Title = "Root", PageType = "folder"
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = Guid.NewGuid(), ParentId = parentId, Segment = "a", Path = "root/a", Title = "A", PageType = "content", SortOrder = 1 }
        });
        // Setting service returns 0 → should fall back to DefaultPageLength (20)
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(0);

        var result = await Sut().Index(parentId, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(20, vm.PageSize);
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
    public void Controller_IsGatedByContentPagesSection()
    {
        var gate = typeof(PageTreeAdminController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal("content-pages", gate!.SectionKey);
    }

    [Fact]
    public void Create_Has_ValidateAntiForgeryToken()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Create));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── GET /admin/pages/{id}/versions/{versionId}/preview ───────────────────

    [Fact]
    public async Task VersionPreview_UnknownNode_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().VersionPreview(id, 1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task VersionPreview_UnknownVersionId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 2, IsCurrent = true, Content = "[]" }
        });

        var result = await Sut().VersionPreview(id, 99);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task VersionPreview_ContentNode_MatchingVersion_ReturnsPreviewView()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "My Page", PageType = "content"
        });
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 5, IsCurrent = false, Content = "[]" }
        });

        var result = await Sut().VersionPreview(id, 5);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Page/Content.cshtml", view.ViewName);
        var vm = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("My Page", vm.Title);
        Assert.Equal("content", vm.PageType);
        Assert.True(vm.IsPreview);
    }

    [Fact]
    public async Task VersionPreview_WikiNode_MatchingVersion_ReturnsPreviewView()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki", Path = "wiki", Title = "Wiki Page", PageType = "wiki"
        });
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 3, IsCurrent = false, Content = "<p>hi</p>" }
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());

        var result = await Sut().VersionPreview(id, 3);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Page/Wiki.cshtml", view.ViewName);
        var vm = Assert.IsType<RenderedPageViewModel>(view.Model);
        Assert.Equal("wiki", vm.PageType);
        Assert.Equal("<p>hi</p>", vm.WikiHtml);
        Assert.True(vm.IsPreview);
    }

    // ── POST /admin/pages/{id}/publish ───────────────────────────────────────

    [Fact]
    public async Task Publish_ValidPost_CallsPublishAsyncAndRedirects()
    {
        var id = Guid.NewGuid();

        // datetime-local form values arrive as strings; the action parses them invariantly to UTC.
        var result = await Sut().Publish(id, 3, "2026-08-01T09:30", null);

        await _service.Received(1).PublishAsync(id, 3,
            Arg.Is<DateTime?>(d => d.HasValue
                && d.Value.Kind == DateTimeKind.Utc
                && d.Value == new DateTime(2026, 8, 1, 9, 30, 0)),
            Arg.Is<DateTime?>(d => d == null),
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit#versions", redirect.Url);
    }

    [Fact]
    public void Publish_Action_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Publish));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Save_NullContent_SavesEmptyString_NotNull()
    {
        var id = Guid.NewGuid();

        await Sut().Save(id, null!);

        // Content column is NOT NULL; a null form value must be coalesced to empty, not passed through.
        await _service.Received(1).SaveWorkingContentAsync(
            id, string.Empty, Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task SaveAndPublish_NullContent_SavesEmptyString_ThenPublishes()
    {
        var id = Guid.NewGuid();

        await Sut().SaveAndPublish(id, null!);

        await _service.Received(1).SaveWorkingContentAsync(
            id, string.Empty, Arg.Any<string>(), Arg.Any<string?>());
        await _service.Received(1).PublishDraftAsync(id, Arg.Any<string?>());
    }

    // ── POST /admin/pages/{id}/publish-draft ─────────────────────────────────

    [Fact]
    public async Task PublishDraft_Post_CallsPublishDraftAsyncAndRedirectsToEdit()
    {
        var id = Guid.NewGuid();

        var result = await Sut().PublishDraft(id);

        await _service.Received(1).PublishDraftAsync(id, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public void PublishDraft_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.PublishDraft));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/unpublish ─────────────────────────────────────

    [Fact]
    public async Task Unpublish_Post_CallsUnpublishAsyncAndRedirectsToEdit()
    {
        var id = Guid.NewGuid();

        var result = await Sut().Unpublish(id);

        await _service.Received(1).UnpublishAsync(id, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public void Unpublish_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Unpublish));
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
    public async Task Edit_WikiNode_SetsIsPublished_True_WhenNodeIsLive()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki", Path = "wiki", Title = "Wiki", PageType = "wiki"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("<p>Content</p>");
        _service.IsPublishedAsync(id).Returns(true);

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<PageTreeAdminWikiEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.IsPublished);
    }

    [Fact]
    public async Task Edit_WikiNode_SetsIsPublished_False_WhenNodeIsDraft()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki", Path = "wiki", Title = "Wiki", PageType = "wiki"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("<p>Draft</p>");
        _service.IsPublishedAsync(id).Returns(false);

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<PageTreeAdminWikiEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(vm.IsPublished);
    }

    [Fact]
    public async Task Edit_ContentNode_SetsIsPublished_True_WhenNodeIsLive()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("[]");
        _service.IsPublishedAsync(id).Returns(true);

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<ContentPageEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.IsPublished);
        Assert.Equal(id, vm.NodeId);
    }

    [Fact]
    public async Task Edit_ContentNode_SetsIsPublished_False_WhenNodeIsDraft()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("[]");
        _service.IsPublishedAsync(id).Returns(false);

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<ContentPageEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(vm.IsPublished);
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

    // ── Breadcrumb (controller builds ancestor chain) ────────────────────────

    [Fact]
    public async Task Index_Root_HasEmptyBreadcrumb()
    {
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(null, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(vm.Breadcrumb);
    }

    [Fact]
    public async Task Index_DirectRootChild_HasEmptyBreadcrumb()
    {
        // A node whose parent is null (i.e., it is itself a root node) has no ancestors.
        var nodeId = Guid.NewGuid();
        _service.GetNodeByIdAsync(nodeId).Returns(new PageNodeDto
        {
            Id = nodeId, Segment = "support", Path = "support", Title = "Support",
            PageType = "folder", ParentId = null
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = nodeId, Segment = "support", Path = "support", Title = "Support",
                    PageType = "folder", SortOrder = 1 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(nodeId, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        // Support is a root node — no ancestors between "Pages" and "Support".
        Assert.Empty(vm.Breadcrumb);
    }

    [Fact]
    public async Task Index_NestedNode_HasAncestorChainRootFirst()
    {
        var rootId    = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var faqId     = Guid.NewGuid();

        _service.GetNodeByIdAsync(faqId).Returns(new PageNodeDto
        {
            Id = faqId, Segment = "faq", Path = "support/section/faq", Title = "FAQ",
            PageType = "wiki", ParentId = sectionId
        });
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>
        {
            new() { Id = rootId,    Segment = "support",  Path = "support",              Title = "Support", PageType = "folder",  SortOrder = 1 },
            new() { Id = sectionId, ParentId = rootId,    Segment = "section", Path = "support/section", Title = "Section", PageType = "folder",  SortOrder = 1 },
            new() { Id = faqId,     ParentId = sectionId, Segment = "faq",     Path = "support/section/faq", Title = "FAQ",     PageType = "wiki",    SortOrder = 1 }
        });
        _settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var result = await Sut().Index(faqId, null, 1);

        var vm = Assert.IsType<PageTreeGridViewModel>(Assert.IsType<ViewResult>(result).Model);
        // Breadcrumb: Support (root) → Section (FAQ's parent); FAQ itself is the plain current crumb.
        Assert.Equal(2, vm.Breadcrumb.Count);
        Assert.Equal(rootId,    vm.Breadcrumb[0].Id);
        Assert.Equal("Support", vm.Breadcrumb[0].Title);
        Assert.Equal(sectionId, vm.Breadcrumb[1].Id);
        Assert.Equal("Section", vm.Breadcrumb[1].Title);
    }

    // ── Move redirect ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_WithParentId_RedirectsToParentGrid()
    {
        var parentId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "parent/page", Title = "Page",
            PageType = "content", ParentId = parentId
        });
        _service.MoveAsync(id, "up").Returns(Task.CompletedTask);

        var result = await Sut().Move(id, "up");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{parentId}", redirect.Url);
    }

    [Fact]
    public async Task Move_WithNoParent_RedirectsToRoot()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "root-page", Path = "root-page", Title = "Root page",
            PageType = "content", ParentId = null
        });
        _service.MoveAsync(id, "down").Returns(Task.CompletedTask);

        var result = await Sut().Move(id, "down");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/pages", redirect.Url);
    }

    // ── POST /admin/pages/{id}/move (original tests retained) ────────────────

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

    // ── POST /admin/pages/{id}/save-and-publish ──────────────────────────────

    [Fact]
    public async Task SaveAndPublish_CallsSaveWorkingContentThenPublishDraftAsync_AndRedirectsToEdit()
    {
        var id = Guid.NewGuid();
        const string rawHtml = "<p>Hello</p>";
        _renderer.RenderHtml(rawHtml).Returns(rawHtml);
        _renderer.StripTagsToPlainText(rawHtml).Returns("Hello");

        var result = await Sut().SaveAndPublish(id, rawHtml);

        await _service.Received(1).SaveWorkingContentAsync(id, rawHtml, "Hello", Arg.Any<string?>());
        await _service.Received(1).PublishDraftAsync(id, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public void SaveAndPublish_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.SaveAndPublish));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/content/save-draft ────────────────────────────

    [Fact]
    public async Task ContentSaveDraft_KnownNode_CallsSaveWorkingContentAsync_AndRedirectsToEdit()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });
        _service.GetWorkingOrLatestContentAsync(id).Returns("[{\"kind\":\"widget\"}]");

        var result = await Sut().ContentSaveDraft(id);

        await _service.Received(1).SaveWorkingContentAsync(
            id, "[{\"kind\":\"widget\"}]", string.Empty, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public async Task ContentSaveDraft_NullContent_SavesEmptyArray_AndRedirectsToEdit()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });
        _service.GetWorkingOrLatestContentAsync(id).Returns((string?)null);

        var result = await Sut().ContentSaveDraft(id);

        await _service.Received(1).SaveWorkingContentAsync(
            id, "[]", string.Empty, Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public async Task ContentSaveDraft_UnknownNode_ReturnsNotFound_DoesNotCallSave()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentSaveDraft(id);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().SaveWorkingContentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public void ContentSaveDraft_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.ContentSaveDraft));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/versions/publish-now ──────────────────────────

    [Fact]
    public async Task PublishNow_PublishesVersionLiveNow_OpenEnded_AndRedirectsToEdit()
    {
        var id = Guid.NewGuid();

        var result = await Sut().PublishNow(id, 3);

        // "Publish" always goes live now (non-null From) with no end date (null To), ignoring any
        // pre-filled row dates. NSubstitute: all DateTime? args must use matchers when one does.
        await _service.Received(1).PublishAsync(
            id, 3,
            Arg.Is<DateTime?>(d => d != null),
            Arg.Is<DateTime?>(d => d == null),
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit#versions", redirect.Url);
    }

    [Fact]
    public void PublishNow_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.PublishNow));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── POST /admin/pages/{id}/versions/unpublish ────────────────────────────

    [Fact]
    public async Task UnpublishVersion_CallsPublishAsync_WithNullWindow_AndRedirectsToVersions()
    {
        var id = Guid.NewGuid();

        var result = await Sut().UnpublishVersion(id, 2);

        await _service.Received(1).PublishAsync(
            id, 2,
            Arg.Is<DateTime?>(d => d == null),
            Arg.Is<DateTime?>(d => d == null),
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit#versions", redirect.Url);
    }

    [Fact]
    public void UnpublishVersion_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.UnpublishVersion));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ── Edit populates Versions on VM ─────────────────────────────────────────

    [Fact]
    public async Task Edit_ContentNode_PopulatesVersions_OnViewModel()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "page", Path = "page", Title = "Page", PageType = "content"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("[]");
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 2, IsCurrent = true,  Content = "" },
            new() { Id = Guid.NewGuid(), VersionId = 1, IsCurrent = false, Content = "" }
        });

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<ContentPageEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, vm.Versions.Count);
    }

    [Fact]
    public async Task Edit_WikiNode_PopulatesVersions_OnViewModel()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "wiki", Path = "wiki", Title = "Wiki", PageType = "wiki"
        });
        _service.GetWorkingOrLatestContentAsync(id).Returns("<p>Hello</p>");
        _service.GetVersionsAsync(id).Returns(new List<PageNodeVersionDto>
        {
            new() { Id = Guid.NewGuid(), VersionId = 3, IsCurrent = false, Content = "" },
            new() { Id = Guid.NewGuid(), VersionId = 2, IsCurrent = true,  Content = "" },
            new() { Id = Guid.NewGuid(), VersionId = 1, IsCurrent = false, Content = "" }
        });

        var result = await Sut().Edit(id);

        var vm = Assert.IsType<PageTreeAdminWikiEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(3, vm.Versions.Count);
    }

    // ── POST /admin/pages/{id}/move-to (page-tree drag/drop) ─────────────────
    // JSON body { newParentId: Guid?, newSortOrder: int } from the admin nav DnD JS.
    // Each MoveNodeResult must translate to the exact HTTP status/shape the JS relies on
    // to keep the tree in sync — a silent 400 with no `error` key would break the client's
    // "cycle" vs "conflict" branching.

    [Fact]
    public async Task MoveTo_NullBody_ReturnsBadRequest_WithoutCallingService()
    {
        var result = await Sut().MoveTo(Guid.NewGuid(), request: null);

        Assert.IsType<BadRequestResult>(result);
        await _service.DidNotReceive().MoveNodeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task MoveTo_ServiceReturnsOk_Returns200_AndForwardsRequestArgs()
    {
        var id = Guid.NewGuid();
        var newParent = Guid.NewGuid();
        _service.MoveNodeAsync(id, newParent, 3, Arg.Any<string?>()).Returns(MoveNodeResult.Ok);

        var result = await Sut().MoveTo(
            id,
            new PageTreeAdminController.MoveNodeRequest { NewParentId = newParent, NewSortOrder = 3 });

        Assert.IsType<OkResult>(result);
        await _service.Received(1).MoveNodeAsync(id, newParent, 3, Arg.Any<string?>());
    }

    [Fact]
    public async Task MoveTo_ServiceReturnsNotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _service.MoveNodeAsync(id, null, 0, Arg.Any<string?>()).Returns(MoveNodeResult.NotFound);

        var result = await Sut().MoveTo(
            id,
            new PageTreeAdminController.MoveNodeRequest { NewParentId = null, NewSortOrder = 0 });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MoveTo_ServiceReturnsCycle_Returns400_WithCycleErrorKey()
    {
        var id = Guid.NewGuid();
        _service.MoveNodeAsync(id, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(MoveNodeResult.Cycle);

        var result = await Sut().MoveTo(
            id,
            new PageTreeAdminController.MoveNodeRequest { NewParentId = id, NewSortOrder = 0 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("cycle", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task MoveTo_ServiceReturnsPathConflict_Returns400_WithPathConflictErrorKey()
    {
        var id = Guid.NewGuid();
        _service.MoveNodeAsync(id, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(MoveNodeResult.PathConflict);

        var result = await Sut().MoveTo(
            id,
            new PageTreeAdminController.MoveNodeRequest { NewParentId = null, NewSortOrder = 0 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("path-conflict", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public void MoveTo_HasValidateAntiForgeryTokenAndHttpPost()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.MoveTo))!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    // ── POST /admin/pages/{id}/content/move-to ───────────────────────────────

    [Fact]
    public async Task ContentMoveTo_KnownNode_CallsMoveToAsync_WithParsedPaths_AndRedirects()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns(new PageNodeDto
            { Id = id, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var result = await Sut().ContentMoveTo(id, "0.2", "0.0");

        await _contentEditor.Received(1).MoveToAsync(
            id,
            Arg.Is<IReadOnlyList<TreeStep>>(p => p.SequenceEqual(new[] { new TreeStep(0, 2) })),
            Arg.Is<IReadOnlyList<TreeStep>>(p => p.SequenceEqual(new[] { new TreeStep(0, 0) })),
            Arg.Any<string?>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{id}/edit", redirect.Url);
    }

    [Fact]
    public async Task ContentMoveTo_UnknownNode_ReturnsNotFound_DoesNotCallEditor()
    {
        var id = Guid.NewGuid();
        _service.GetNodeByIdAsync(id).Returns((PageNodeDto?)null);

        var result = await Sut().ContentMoveTo(id, "0.0", "0.1");

        Assert.IsType<NotFoundResult>(result);
        await _contentEditor.DidNotReceive().MoveToAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<TreeStep>>(),
            Arg.Any<IReadOnlyList<TreeStep>>(), Arg.Any<string?>());
    }

    [Fact]
    public void ContentMoveTo_HasValidateAntiForgeryTokenAndHttpPost()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.ContentMoveTo))!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    // ── POST /admin/pages/{id}/copy ──────────────────────────────────────────

    [Fact]
    public async Task Copy_KnownPage_CallsCopyPageAsync_RedirectsToNewPagesPropertiesTab()
    {
        var sourceId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        _service.CopyPageAsync(sourceId, Arg.Any<string?>()).Returns(new PageNodeDto
        {
            Id = newId, Segment = "help-copy", Path = "help-copy", Title = "Help - Copy", PageType = "content"
        });

        var result = await Sut().Copy(sourceId);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/pages/{newId}/edit#properties", redirect.Url);
        await _service.Received(1).CopyPageAsync(sourceId, Arg.Any<string?>());
    }

    [Fact]
    public async Task Copy_UnknownPage_Returns404()
    {
        var sourceId = Guid.NewGuid();
        _service.CopyPageAsync(sourceId, Arg.Any<string?>()).Returns((PageNodeDto?)null);

        var result = await Sut().Copy(sourceId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Copy_HasValidateAntiForgeryTokenAndHttpPost()
    {
        var method = typeof(PageTreeAdminController).GetMethod(nameof(PageTreeAdminController.Copy))!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }
}
