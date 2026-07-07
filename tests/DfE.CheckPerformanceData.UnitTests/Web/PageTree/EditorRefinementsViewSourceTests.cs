namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

// Static Razor-source assertions for the editor UI-polish pass:
// - _ActionIcon.cshtml partial exists and contains an SVG
// - _EditColumn.cshtml uses _ActionIcon, icon buttons, onsubmit confirm, region-first order, toolbar delete class
// - _EditWidget.cshtml edit summary references _ActionIcon with "edit"
// - Edit.cshtml style block contains the .cpb-region #f3f2f1 rule
// - _VersionHistoryList.cshtml contains "Modified by" and "UpdatedBy"
//
// Pattern mirrors ContentPageEditorViewSourceTests and PageTreeAdminEditPublishViewSourceTests.
public sealed class EditorRefinementsViewSourceTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        // File lives at {repo}/tests/.../Web/PageTree/EditorRefinementsViewSourceTests.cs
        // Four levels up reaches the repo root.
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string ReadSharedPageTree(string partial) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "PageTree", partial));

    private static string ReadSharedContentPages(string partial) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "ContentPages", partial));

    private static string ReadContentPage(string view) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "ContentPage", view));

    private static string ReadPageTreeAdmin(string view) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "PageTreeAdmin", view));

    // ── _ActionIcon.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void ActionIcon_Exists_AndContainsSvg()
    {
        var src = ReadSharedPageTree("_ActionIcon.cshtml");
        Assert.Contains("<svg", src);
    }

    [Fact]
    public void ActionIcon_ContainsAriaHidden()
    {
        var src = ReadSharedPageTree("_ActionIcon.cshtml");
        Assert.Contains("aria-hidden=\"true\"", src);
    }

    // ── _EditColumn.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditColumn_UsesActionIcon_Partial()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("_ActionIcon", src);
    }

    [Fact]
    public void EditColumn_ReferencesArrowUpward_ViaPartialCall()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("arrow_upward", src);
    }

    [Fact]
    public void EditColumn_ReferencesArrowDownward_ViaPartialCall()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("arrow_downward", src);
    }

    [Fact]
    public void EditColumn_DeleteForm_HasOnsubmitConfirm()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("onsubmit=\"return confirm(", src);
    }

    [Fact]
    public void EditColumn_DeleteForm_HasToolbarDeleteClass()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("cpb-toolbar__delete", src);
    }

    [Fact]
    public void AddHere_RegionFormAppearsBeforeWidgetForm()
    {
        // The add-forms now live in _AddHere.cshtml. Region form must still appear before the
        // widget form so the "add region" option is not buried under "add widget".
        var src = ReadSharedContentPages("_AddHere.cshtml");
        var regionIdx = src.IndexOf("regionLayout", StringComparison.Ordinal);
        var widgetIdx = src.IndexOf("widgetType", StringComparison.Ordinal);
        Assert.True(regionIdx >= 0, "Expected regionLayout select in _AddHere.cshtml");
        Assert.True(widgetIdx >= 0, "Expected widgetType select in _AddHere.cshtml");
        Assert.True(regionIdx < widgetIdx, "Add-region form (regionLayout) must appear before Add-widget form (widgetType)");
    }

    [Fact]
    public void EditColumn_MoveFormActions_StillUseActionBase()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/move\"", src);
    }

    [Fact]
    public void EditColumn_DeleteFormAction_StillUsesActionBase()
    {
        var src = ReadSharedContentPages("_EditColumn.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/delete\"", src);
    }

    // ── _EditWidget.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditWidget_EditSummary_ReferencesActionIconWithEdit()
    {
        var src = ReadSharedContentPages("_EditWidget.cshtml");
        Assert.Contains("_ActionIcon", src);
        Assert.Contains("\"edit\"", src);
    }

    // ── Edit.cshtml style block ──────────────────────────────────────────────

    [Fact]
    public void Edit_StyleBlock_CpbRegion_HasLightGreyBackground()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains(".cpb-region", src);
        Assert.Contains("#f3f2f1", src);
        // Ensure the #f3f2f1 appears in a .cpb-region rule context (not just .cpb-preview).
        var regionIdx = src.IndexOf(".cpb-region", StringComparison.Ordinal);
        var greyIdx = src.IndexOf("#f3f2f1", regionIdx, StringComparison.Ordinal);
        Assert.True(greyIdx >= 0, ".cpb-region rule must contain #f3f2f1");
    }

    [Fact]
    public void Edit_StyleBlock_ToolbarFlex_Present()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains(".cpb-toolbar", src);
        Assert.Contains("display: flex", src);
    }

    [Fact]
    public void Edit_StyleBlock_ToolbarDelete_MarginsDelete()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains(".cpb-toolbar__delete", src);
        Assert.Contains("margin-left: auto", src);
    }

    // ── _VersionHistoryList.cshtml ───────────────────────────────────────────

    [Fact]
    public void VersionHistoryList_HasModifiedByColumn()
    {
        var src = ReadPageTreeAdmin("_VersionHistoryList.cshtml");
        Assert.Contains("Modified by", src);
    }

    [Fact]
    public void VersionHistoryList_CellRendersUpdatedBy()
    {
        var src = ReadPageTreeAdmin("_VersionHistoryList.cshtml");
        Assert.Contains("UpdatedBy", src);
    }
}
