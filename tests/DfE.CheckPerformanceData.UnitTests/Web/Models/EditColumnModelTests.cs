using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Models.Guidance;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Models;

// The editor renders the tree recursively; each level must hand its children the right path so the
// add/move/delete forms target the correct node. This pins that path arithmetic: at the root a node's
// path is just its index; inside a region's column it is the region's path plus (column, index).
public class EditColumnModelTests
{
    private static WidgetNode W() => new() { Type = "divider" };

    [Fact]
    public void Root_PathOf_IsIndexInColumnZero()
    {
        var model = new EditColumnModel("/content-page/ks4", [W(), W(), W()], [], 0);

        Assert.Equal("0.2", TreePath.Format(model.PathOf(2)));
    }

    [Fact]
    public void Root_AppendPath_TargetsEndOfColumn()
    {
        var model = new EditColumnModel("/content-page/ks4", [W(), W(), W()], [], 0);

        Assert.Equal("0.3", TreePath.Format(model.AppendPath()));
    }

    [Fact]
    public void Nested_PathOf_IsRegionPathPlusColumnAndIndex()
    {
        // This column is column 2 of a region that itself sits at root index 1.
        var model = new EditColumnModel("/content-page/ks4", [W(), W()], [new TreeStep(0, 1)], 2);

        Assert.Equal("0.1-2.0", TreePath.Format(model.PathOf(0)));
        Assert.Equal("0.1-2.1", TreePath.Format(model.PathOf(1)));
    }

    [Fact]
    public void Nested_AppendPath_TargetsEndOfTheNestedColumn()
    {
        var model = new EditColumnModel("/content-page/ks4", [W(), W()], [new TreeStep(0, 1)], 2);

        Assert.Equal("0.1-2.2", TreePath.Format(model.AppendPath()));
    }
}
