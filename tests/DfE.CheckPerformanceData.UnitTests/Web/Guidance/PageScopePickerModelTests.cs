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

    // ----- What the picker shows ticked -----

    private static readonly Guid Guidance = new("00000000-cd94-4a01-8f01-000000000004");
    private static readonly Guid Post16 = new("00000000-cd94-4a01-8f01-0000000000a1");
    private static readonly Guid Help = new("00000000-cd94-4a01-8f01-000000000003");

    private static PageScopePickerModel Site() => PageScopePickerModel.From(
    [
        Node(Guidance, null, "guidance", "Guidance", sort: 1),
        Node(Post16, Guidance, "guidance/post-16", "Post-16"),
        Node(Help, null, "help", "Help", sort: 2),
    ]);

    // Each page's checkbox posts its id, and pages are ticked by the ids the widget stores.
    [Fact]
    public void ItemsFor_TicksThePagesWhoseIdsTheWidgetStores()
    {
        var items = Site().ItemsFor(pageIds: $"{Help},{Post16}", scopePaths: null);

        Assert.Equal([Guidance.ToString(), Post16.ToString(), Help.ToString()], items.Select(i => i.Value).ToList());
        Assert.Equal([false, true, true], items.Select(i => i.Ticked).ToList());
        Assert.All(items, i => Assert.Null(i.Note));
        Assert.Equal("guidance/post-16", items[1].Path);
        Assert.Equal(1, items[1].Depth);
    }

    // A widget saved when pages were stored by path still shows its pages ticked.
    [Fact]
    public void ItemsFor_AWidgetWithOnlyAPathScope_TicksThePagesAtThosePaths()
    {
        var items = Site().ItemsFor(pageIds: null, scopePaths: "/Guidance/post-16/,help");

        Assert.Equal([false, true, true], items.Select(i => i.Ticked).ToList());
        Assert.Equal(3, items.Count);
    }

    // A stored id whose page has gone stays visible and ticked, so saving cannot silently drop it.
    [Fact]
    public void ItemsFor_AStoredIdWithNoPage_IsListedTicked_AsAPageThatNoLongerExists()
    {
        var gone = new Guid("00000000-cd94-4a01-8f01-0000000000ff");

        var item = Site().ItemsFor(pageIds: $"{Help},{gone}", scopePaths: null).Last();

        Assert.Equal(gone.ToString(), item.Value);
        Assert.True(item.Ticked);
        Assert.Null(item.Path);
        Assert.Equal("page no longer exists", item.Note);
    }

    [Fact]
    public void ItemsFor_AStoredPathWithNoPage_IsListedTicked_AsBefore()
    {
        var item = Site().ItemsFor(pageIds: null, scopePaths: "help,guidance/gone").Last();

        Assert.Equal("guidance/gone", item.Value);
        Assert.Equal("/guidance/gone", item.Label);
        Assert.True(item.Ticked);
        Assert.Equal("no page at this path", item.Note);
    }
}
