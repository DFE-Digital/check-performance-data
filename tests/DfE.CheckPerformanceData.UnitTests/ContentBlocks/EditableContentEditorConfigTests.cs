namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

// Static Razor-source assertions for the EditableContent WYSIWYG (TinyMCE) config.
// The rich-text editor is configured inline in the component's Default.cshtml; these
// tests pin the editor's authoring affordances. Rendered editor behaviour is verified
// live (Playwright) in the admin UI.
public sealed class EditableContentEditorConfigTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string EditorView() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web",
            "Views", "Shared", "Components", "EditableContent", "Default.cshtml"));

    // Item 6: a "last updated" date is metadata, not a status — the tag styling
    // affordance is removed so editors can't apply a govuk-tag to a date.
    [Fact]
    public void Editor_DoesNotOfferLastUpdatedTagStyle()
    {
        Assert.DoesNotContain("Last updated tag", EditorView());
    }

    // Item 4: editors can apply GDS warning-text to a paragraph. A style_formats
    // wrapper can't produce the component's required inner structure, so it's a
    // dedicated toolbar button instead.
    [Fact]
    public void Editor_RegistersWarningTextButton_OnToolbar()
    {
        var src = EditorView();
        Assert.Contains("addButton('warningtext'", src);
        Assert.Contains("warningtext", src.Split("toolbar:")[1].Split('\n')[0]);
    }

    // Item 4: the button must insert canonical, accessible govuk-warning-text markup —
    // the "!" icon and the visually-hidden "Warning" prefix for screen readers.
    [Fact]
    public void Editor_WarningTextButton_InsertsCanonicalAccessibleMarkup()
    {
        var src = EditorView();
        Assert.Contains("govuk-warning-text__icon", src);
        Assert.Contains("govuk-warning-text__text", src);
        Assert.Contains("govuk-visually-hidden\">Warning", src);
    }

    // The non-functional style_formats wrapper for warning text must be gone.
    [Fact]
    public void Editor_DoesNotKeepBrokenWarningTextStyleFormat()
    {
        Assert.DoesNotContain("title: 'Warning text'", EditorView());
    }

    // Regression: the designer's kept callouts (items 1 & 2) and inset text remain.
    [Fact]
    public void Editor_StillOffersPublishedNoticeAndInsetStyles()
    {
        var src = EditorView();
        Assert.Contains("cypmd-published", src);
        Assert.Contains("cypmd-notice", src);
        Assert.Contains("govuk-inset-text", src);
    }
}
