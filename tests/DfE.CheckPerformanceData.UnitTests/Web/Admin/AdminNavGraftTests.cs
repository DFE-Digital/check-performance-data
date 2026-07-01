using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

public sealed class AdminNavGraftTests
{
    private record FakeEntry(
        string Key,
        string? ParentKey,
        string Title,
        string Description,
        string Url,
        bool Enabled,
        int Order,
        string HttpMethod) : IAdminNavEntry;

    private static IReadOnlyList<AdminNavNodeViewModel> BuildForestWithContentPages()
    {
        var entries = new List<IAdminNavEntry>
        {
            new FakeEntry(AdminNavKeys.CmsAdmin, null, "CMS", "", "/admin/cms", true, 1, "GET"),
            new FakeEntry(AdminNavKeys.ContentPages, AdminNavKeys.CmsAdmin, "Pages", "", "/admin/pages", true, 10, "GET"),
        };
        return AdminNavNodeViewModel.BuildForest(entries);
    }

    private static AdminNavNodeViewModel FindNode(IReadOnlyList<AdminNavNodeViewModel> forest, string key)
    {
        var found = FindNodeRecursive(forest, key);
        if (found is null)
            throw new InvalidOperationException($"Node '{key}' not found in forest.");
        return found;
    }

    private static AdminNavNodeViewModel? FindNodeRecursive(IReadOnlyList<AdminNavNodeViewModel> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Entry.Key == key) return node;
            var found = FindNodeRecursive(node.Children, key);
            if (found is not null) return found;
        }
        return null;
    }

    // --- GraftPageTree_GraftsPageTreeUnderContentPages ---

    [Fact]
    public void GraftPageTree_GraftsPageTreeUnderContentPages()
    {
        var forest = BuildForestWithContentPages();
        var homeId = Guid.NewGuid();
        var aboutId = Guid.NewGuid();
        var pageTree = new List<PageTreeNode>
        {
            new(homeId, "Home", "/home", "content", true, Array.Empty<PageTreeNode>()),
            new(aboutId, "About", "/about", "wiki", false, Array.Empty<PageTreeNode>()),
        };

        var result = AdminNavNodeViewModel.GraftPageTree(forest, pageTree);

        var contentPages = FindNode(result, AdminNavKeys.ContentPages);
        Assert.Equal(2, contentPages.Children.Count);
        Assert.Equal($"page-{homeId}", contentPages.Children[0].Entry.Key);
        Assert.Equal($"/admin/pages/{homeId}", contentPages.Children[0].Entry.Url);
        Assert.Equal($"page-{aboutId}", contentPages.Children[1].Entry.Key);
        Assert.Equal($"/admin/pages/{aboutId}", contentPages.Children[1].Entry.Url);
    }

    // --- GraftPageTree_RecursiveChildren_MapCorrectly ---

    [Fact]
    public void GraftPageTree_RecursiveChildren_MapCorrectly()
    {
        var forest = BuildForestWithContentPages();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var pageTree = new List<PageTreeNode>
        {
            new(parentId, "Parent", "/parent", "folder", false, new List<PageTreeNode>
            {
                new(childId, "Child", "/parent/child", "content", true, Array.Empty<PageTreeNode>()),
            }),
        };

        var result = AdminNavNodeViewModel.GraftPageTree(forest, pageTree);

        var contentPages = FindNode(result, AdminNavKeys.ContentPages);
        Assert.Single(contentPages.Children);

        var parentNode = contentPages.Children[0];
        Assert.Equal($"page-{parentId}", parentNode.Entry.Key);
        Assert.Equal(AdminNavKeys.ContentPages, parentNode.Entry.ParentKey);
        Assert.Single(parentNode.Children);

        var childNode = parentNode.Children[0];
        Assert.Equal($"page-{childId}", childNode.Entry.Key);
        Assert.Equal($"page-{parentId}", childNode.Entry.ParentKey);
    }

    // --- GraftPageTree_EmptyPageTree_ContentPagesHasNoChildren ---

    [Fact]
    public void GraftPageTree_EmptyPageTree_ContentPagesHasNoChildren()
    {
        var forest = BuildForestWithContentPages();

        var result = AdminNavNodeViewModel.GraftPageTree(forest, Array.Empty<PageTreeNode>());

        var contentPages = FindNode(result, AdminNavKeys.ContentPages);
        Assert.Empty(contentPages.Children);
    }

    // --- GraftPageTree_NoContentPagesNode_ReturnsForestUnchanged ---

    [Fact]
    public void GraftPageTree_NoContentPagesNode_ReturnsForestUnchanged()
    {
        var entries = new List<IAdminNavEntry>
        {
            new FakeEntry("other-root", null, "Other", "", "/admin/other", true, 1, "GET"),
        };
        var forest = AdminNavNodeViewModel.BuildForest(entries);
        var pageTree = new List<PageTreeNode>
        {
            new(Guid.NewGuid(), "Home", "/home", "content", true, Array.Empty<PageTreeNode>()),
        };

        var result = AdminNavNodeViewModel.GraftPageTree(forest, pageTree);

        Assert.Single(result);
        Assert.Equal("other-root", result[0].Entry.Key);
        Assert.Empty(result[0].Children);
    }
}
