using System.Reflection;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Pins the controller's thin wiring: GetTreeAsync rows are handed to PageTreeBuilder, the
// resulting tree is passed as view model, and the security contract (editor-role gate) holds.
public sealed class PageTreeAdminControllerTests
{
    private readonly IPageNodeService _service = Substitute.For<IPageNodeService>();

    private PageTreeAdminController Sut() => new(_service);

    // --- Index_ReturnsView_WithBuiltTree ---

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

    // --- Index_BuildsNestedTree_WhenChildRowsProvided ---

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

    // --- Index_ReturnsEmptyList_WhenNoRows ---

    [Fact]
    public async Task Index_ReturnsEmptyList_WhenNoRows()
    {
        _service.GetTreeAsync().Returns(new List<PageNodeTreeItemDto>());

        var result = await Sut().Index();

        var view  = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<PageTreeNode>>(view.Model);
        Assert.Empty(model);
    }

    // --- Controller_IsGatedToTheEditorRole ---

    [Fact]
    public void Controller_IsGatedToTheEditorRole()
    {
        var authorize = typeof(PageTreeAdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(WikiConstants.EditorRole, authorize!.Roles);
    }
}
