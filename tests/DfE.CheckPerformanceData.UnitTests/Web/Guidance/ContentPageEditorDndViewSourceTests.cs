namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

// Static Razor-source assertions for the drag-and-drop reordering markup added to
// _EditColumn.cshtml, _EditWidget.cshtml, and Edit.cshtml.
// Pattern mirrors ContentPageEditorViewSourceTests: read .cshtml source as text.
// These catch the most likely regressions — missing data attributes, lost drag handle,
// or the DnD script reference being dropped from Edit.cshtml.
public sealed class ContentPageEditorDndViewSourceTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string ViewsRoot() =>
        Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views");

    private static string ReadShared(string partial) =>
        File.ReadAllText(Path.Combine(ViewsRoot(), "Shared", "ContentPages", partial));

    private static string ReadContentPage(string view) =>
        File.ReadAllText(Path.Combine(ViewsRoot(), "ContentPage", view));

    // ── _EditColumn.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditColumn_NodeDiv_HasDataNodePathAttribute()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("data-node-path=\"@path\"", src);
    }

    [Fact]
    public void EditColumn_ColumnWrapper_HasDataColumnPathAttribute()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("data-column-path=\"@appendPath\"", src);
    }

    [Fact]
    public void EditColumn_RegionCaption_HasDragHandle()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("cpb-drag-handle", src);
        Assert.Contains("draggable=\"true\"", src);
    }

    // ── _EditWidget.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditWidget_Caption_HasDragHandle()
    {
        var src = ReadShared("_EditWidget.cshtml");
        Assert.Contains("cpb-drag-handle", src);
        Assert.Contains("draggable=\"true\"", src);
    }

    // ── Edit.cshtml ──────────────────────────────────────────────────────────

    [Fact]
    public void Edit_RootElement_HasDataCpbNodeId()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains("data-cpb-node-id=\"@Model.NodeId\"", src);
    }

    [Fact]
    public void Edit_ReferencesContentEditorDndScript()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains("content-editor-dnd", src);
    }
}
