using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.Guidance;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

// The page picker lists the site's pages as the tree an editor knows: parents before their
// children, siblings in their menu order, each option carrying its depth so the list can indent.
public sealed class PageScopePickerModelTests
{
    private static PageNodeTreeItemDto Node(
        Guid id, Guid? parent, string path, string title, int sort = 0, string? pageName = null) =>
        new()
        {
            Id = id, ParentId = parent, Segment = path.Split('/').Last(), Path = path, Title = title,
            PageName = pageName, SortOrder = sort, PageType = "content", CreatedDate = new DateTime(2026, 1, 1)
        };

    [Fact]
    public void From_ListsParentsBeforeChildren_InSiblingOrder_WithDepth()
    {
        var guidance = Guid.NewGuid();
        var ks4 = Guid.NewGuid();
        var sixteen = Guid.NewGuid();
        var dates = Guid.NewGuid();
        var tree = new List<PageNodeTreeItemDto>
        {
            Node(dates, ks4, "guidance/ks4/dates", "Dates"),
            Node(sixteen, guidance, "guidance/16-to-19", "16 to 19", sort: 2),
            Node(ks4, guidance, "guidance/ks4", "KS4", sort: 1),
            Node(guidance, null, "guidance", "Guidance"),
        };

        var options = PageScopePickerModel.From(tree).Options;

        Assert.Equal(
            ["guidance", "guidance/ks4", "guidance/ks4/dates", "guidance/16-to-19"],
            options.Select(o => o.Path).ToList());
        Assert.Equal([0, 1, 2, 1], options.Select(o => o.Depth).ToList());
    }

    [Fact]
    public void From_LabelsAPageByItsNavigationName_WhenItHasOne()
    {
        var id = Guid.NewGuid();
        var options = PageScopePickerModel.From(
            [Node(id, null, "results", "Results enquiries guidance", pageName: "Results enquiries")]).Options;

        Assert.Equal("Results enquiries", Assert.Single(options).Label);
    }
}
