namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

// Static Razor-source assertions for the content-page editor partials after the Slug→ActionBase
// generalization. Pattern mirrors LayoutRenderTests: read the .cshtml source as text and assert
// on the template text. These catch the most likely regressions — accidentally leaving a hard-coded
// /content-page/@Model.Slug/ in one of the partials, or losing the ShowInlinePublish guard.
//
// They do NOT test rendered output (that needs a full MVC harness) but they verify the template
// source sends form actions to @Model.ActionBase rather than the old slug-keyed path.
public sealed class ContentPageEditorViewSourceTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        // File lives at {repo}/tests/.../Web/Guidance/ContentPageEditorViewSourceTests.cs
        // Four levels up reaches the repo root.
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ViewsRoot() =>
        Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views");

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string ReadShared(string partial) =>
        File.ReadAllText(Path.Combine(ViewsRoot(), "Shared", "ContentPages", partial));

    private static string ReadContentPage(string view) =>
        File.ReadAllText(Path.Combine(ViewsRoot(), "ContentPage", view));

    // ── _EditColumn.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditColumn_UsesActionBase_ForMoveFormAction()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/move\"", src);
    }

    [Fact]
    public void EditColumn_UsesActionBase_ForDeleteFormAction()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/delete\"", src);
    }

    [Fact]
    public void EditColumn_UsesActionBase_ForAddFormActions()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/add\"", src);
    }

    [Fact]
    public void EditColumn_ThreadsActionBase_IntoNestedColumnModel()
    {
        var src = ReadShared("_EditColumn.cshtml");
        // The recursive sub-partial call must pass ActionBase, not Slug.
        Assert.Contains("Model.ActionBase, region.Columns[c]", src);
    }

    [Fact]
    public void EditColumn_ThreadsActionBase_IntoWidgetModel()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.Contains("new EditWidgetModel(Model.ActionBase,", src);
    }

    [Fact]
    public void EditColumn_DoesNotReferenceSlug()
    {
        var src = ReadShared("_EditColumn.cshtml");
        Assert.DoesNotContain("Model.Slug", src);
        Assert.DoesNotContain("/content-page/", src);
    }

    // ── _EditWidget.cshtml ───────────────────────────────────────────────────

    [Fact]
    public void EditWidget_UsesActionBase_ForWidgetFormAction()
    {
        var src = ReadShared("_EditWidget.cshtml");
        Assert.Contains("action=\"@Model.ActionBase/widget\"", src);
    }

    [Fact]
    public void EditWidget_DoesNotReferenceSlug()
    {
        var src = ReadShared("_EditWidget.cshtml");
        Assert.DoesNotContain("Model.Slug", src);
        Assert.DoesNotContain("/content-page/", src);
    }

    // ── Edit.cshtml ──────────────────────────────────────────────────────────

    [Fact]
    public void Edit_PassesActionBase_ToEditColumnPartial()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains("new EditColumnModel(Model.ActionBase,", src);
    }

    [Fact]
    public void Edit_ShowInlinePublishGuard_WrapsPublishForm()
    {
        var src = ReadContentPage("Edit.cshtml");
        Assert.Contains("Model.ShowInlinePublish", src);
        // The publish button must be inside the guard.
        var guardIdx = src.IndexOf("Model.ShowInlinePublish", StringComparison.Ordinal);
        var publishIdx = src.IndexOf("Save and publish", StringComparison.Ordinal);
        Assert.True(guardIdx >= 0 && publishIdx > guardIdx,
            "Publish button must appear after the ShowInlinePublish guard.");
    }

    [Fact]
    public void Edit_PreviewLink_UsesPagePath()
    {
        var src = ReadContentPage("Edit.cshtml");
        // The preview anchor links to the public page via PagePath (PagePath is non-nullable
        // for all page-tree nodes, so a null guard is not required here).
        Assert.Contains("/@Model.PagePath", src);
        Assert.Contains("View page", src);
    }
}
