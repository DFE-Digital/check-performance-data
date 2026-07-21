namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Static-Razor contract for the widget editor form. The behaviour that would need a
// full render harness (form-post round-trips, prop coercion) is covered by other
// tests; here we pin the form fields each widget branch exposes so a future edit
// can't quietly drop them.
public sealed class WidgetEditorContractTests
{
    private static readonly string Editor = ReadEditorView();

    // ----- Results widget -----

    [Fact]
    public void Results_EditorExposesScopeField()
    {
        // Manual step 3, 17-18.
        Assert.Contains("props[scope]", ResultsBranch());
    }

    [Fact]
    public void Results_EditorExposesEmptyTextField()
    {
        // Manual step 3, 12.
        Assert.Contains("props[emptyText]", ResultsBranch());
    }

    [Fact]
    public void Results_EditorMentionsWikiPageLengthSetting()
    {
        // Manual step 3: the editor tells the author where results-per-page is controlled.
        Assert.Contains("Wiki:PageLength", ResultsBranch());
    }

    [Fact]
    public void Results_EditorDoesNotExposeIncludePagesToggle()
    {
        // Merged view — no per-corpus filter surfaced to the editor.
        Assert.DoesNotContain("props[includePages]", ResultsBranch());
        Assert.DoesNotContain("props[includeContentBlocks]", ResultsBranch());
    }

    [Fact]
    public void Results_EditorDoesNotExposeMaxPerType()
    {
        // Page size comes from the setting, not a widget prop.
        Assert.DoesNotContain("props[maxPerType]", ResultsBranch());
    }

    // ----- Search widget (companion) -----

    [Fact]
    public void Search_EditorExposesScopeField()
    {
        // Manual step 26 + 23: the previously JSON-only scope prop is now editable.
        Assert.Contains("props[scope]", SearchBranch());
    }

    [Fact]
    public void Search_EditorExposesAction_Label_Placeholder_Button()
    {
        // Existing fields must still be present.
        var b = SearchBranch();
        Assert.Contains("props[label]", b);
        Assert.Contains("props[action]", b);
        Assert.Contains("props[placeholder]", b);
        Assert.Contains("props[buttonText]", b);
    }

    // ----- helpers -----

    // Returns the slice of the editor between the `case "results":` line and its `break;`.
    private static string ResultsBranch() => BranchSlice("case \"results\":");
    private static string SearchBranch() => BranchSlice("case \"search\":");

    private static string BranchSlice(string caseMarker)
    {
        var start = Editor.IndexOf(caseMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Editor view missing '{caseMarker}'.");
        var breakIndex = Editor.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(breakIndex > start, $"Editor view missing 'break;' for {caseMarker}.");
        return Editor.Substring(start, breakIndex - start);
    }

    private static string ReadEditorView()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var path = Path.Combine(
            solutionRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views",
            "Shared", "ContentPages", "_EditWidget.cshtml");
        return File.ReadAllText(path);
    }

    private static string FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetDirectories("src").Length > 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate solution root from {startDir} (no .slnx or src/ found in any ancestor).");
    }
}
