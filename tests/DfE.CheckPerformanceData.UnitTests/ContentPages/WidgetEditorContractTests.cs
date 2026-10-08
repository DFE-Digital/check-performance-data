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
    public void Results_EditorExposesEmptyTextField()
    {
        // Manual step 3, 12.
        Assert.Contains("props[emptyText]", ResultsBranch());
    }

    [Fact]
    public void Results_EditorMentionsCmsPageLengthSetting()
    {
        // Manual step 3: the editor tells the author where results-per-page is controlled.
        Assert.Contains("CMS:PageLength", ResultsBranch());
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
    public void Search_EditorExposesAction_Label_Placeholder_Button()
    {
        // Existing fields must still be present.
        var b = SearchBranch();
        Assert.Contains("props[label]", b);
        Assert.Contains("props[action]", b);
        Assert.Contains("props[placeholder]", b);
        Assert.Contains("props[buttonText]", b);
    }

    [Fact]
    public void Search_EditorExposesTheThreeSearchTargets()
    {
        var b = SearchBranch();
        Assert.Contains("props[searchIn]", b);
        Assert.Contains("value=\"site\"", b);
        Assert.Contains("value=\"path\"", b);
        Assert.Contains("value=\"page\"", b);
    }

    [Fact]
    public void Search_EditorExposesTheInstantTickBox()
    {
        var b = SearchBranch();
        Assert.Contains("props[instant]", b);
        Assert.Contains("type=\"checkbox\"", b);
    }

    // "Show search button" only means something with instant search, so it sits in the block the
    // instant tick box reveals. An unticked box posts nothing, so a hidden "false" follows the label:
    // the browser posts in DOM order and the first value wins.
    [Fact]
    public void Search_EditorRevealsShowSearchButton_UnderInstantSearch()
    {
        var b = SearchBranch();
        var reveal = b.IndexOf("data-cpb-search-instantfields", StringComparison.Ordinal);
        var checkbox = b.IndexOf("name=\"props[showButton]\" type=\"checkbox\"", StringComparison.Ordinal);
        var label = b.IndexOf("Show search button", StringComparison.Ordinal);
        var hidden = b.IndexOf("<input type=\"hidden\" name=\"props[showButton]\" value=\"false\" />", StringComparison.Ordinal);

        Assert.True(reveal >= 0, "Search editor has no instant-search reveal.");
        Assert.True(checkbox > reveal, "Show search button must sit inside the instant-search reveal.");
        Assert.True(label > checkbox, "Show search button has no label after its checkbox.");
        Assert.True(hidden > label, "Unticking Show search button would post nothing without a hidden false after the label.");
    }

    // A widget saved before the option existed has no stored value and shows the button, so the
    // box starts ticked unless "false" is stored.
    [Fact]
    public void Search_EditorTicksShowSearchButton_UnlessStoredFalse()
    {
        Assert.Contains("GetBool(\"showButton\") ?? true", SearchBranch());
    }

    // Where the button goes matters with or without instant search, so the option sits outside
    // the instant-search reveal. As with the other tick boxes, a hidden "false" after the label is
    // what makes unticking stick.
    [Fact]
    public void Search_EditorOffersTheButtonBelowTheBox()
    {
        var b = SearchBranch();
        var checkbox = b.IndexOf("name=\"props[buttonBelow]\" type=\"checkbox\"", StringComparison.Ordinal);
        var label = b.IndexOf("Put the button below the search box", StringComparison.Ordinal);
        var hidden = b.IndexOf("<input type=\"hidden\" name=\"props[buttonBelow]\" value=\"false\" />", StringComparison.Ordinal);

        Assert.True(checkbox >= 0, "Search editor has no tick box for the button position.");
        Assert.True(label > checkbox, "The button position tick box has no label after it.");
        Assert.True(hidden > label, "Unticking would post nothing without a hidden false after the label.");
        Assert.Contains("GetBool(\"buttonBelow\") ?? false", b);
    }

    [Fact]
    public void Search_EditorExposesNoResultsCopy()
    {
        Assert.Contains("props[noResultsText]", SearchBranch());
    }

    [Fact]
    public void Search_EditorKeepsTheFallbackFieldsVisible()
    {
        // Action and button text are what a no-JS visitor uses even when instant search is on,
        // so they must not be hidden behind the instant toggle.
        var b = SearchBranch();
        Assert.Contains("props[action]", b);
        Assert.Contains("props[buttonText]", b);
    }

    // ----- Page picker (search + results scope) -----

    [Fact]
    public void Search_EditorPicksScopePagesWithACheckboxPerPage()
    {
        // The picker posts every ticked page under one repeated field; the controller joins them.
        var b = SearchBranch();
        Assert.Contains("_PageScopePicker", b);
        Assert.DoesNotContain("name=\"props[scope]\"", b);
    }

    [Fact]
    public void Results_EditorUsesTheSamePagePicker()
    {
        var b = ResultsBranch();
        Assert.Contains("_PageScopePicker", b);
        Assert.DoesNotContain("name=\"props[scope]\"", b);
    }

    // Both pickers are given the pages the widget stores by id as well as any old path scope.
    [Fact]
    public void Pickers_AreGivenTheStoredPageIdsAndPathScope()
    {
        Assert.Contains("w.GetString(\"scope\"), w.GetString(\"scopePageIds\")", SearchBranch());
        Assert.Contains("w.GetString(\"scope\"), w.GetString(\"scopePageIds\")", ResultsBranch());
    }

    [Fact]
    public void PagePicker_PostsTicksAsScopePagesCheckboxes_OfTheSitePageTree()
    {
        var picker = File.ReadAllText(Path.Combine(
            FindSolutionRoot(AppContext.BaseDirectory),
            "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "ContentPages", "_PageScopePicker.cshtml"));

        Assert.Contains("name=\"scopePages\"", picker);
        Assert.Contains("type=\"checkbox\"", picker);
        Assert.Contains("govuk-checkboxes", picker);
        Assert.Contains(".ItemsFor(Model.PageIds, Model.Scope)", picker);
    }

    [Fact]
    public void PagePicker_PostsAMarker_SoTheSaveKnowsTheTicksAreTheScope()
    {
        var picker = File.ReadAllText(Path.Combine(
            FindSolutionRoot(AppContext.BaseDirectory),
            "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "ContentPages", "_PageScopePicker.cshtml"));

        Assert.Contains("<input type=\"hidden\" name=\"scopePicker\" value=\"true\" />", picker);
    }

    // ----- PageNav widget -----

    [Fact]
    public void PageNav_EditorExposesAHeadingLevelBoxForEachOfH1ToH6()
    {
        // The boxes come out of a loop, so the source carries the bounds and the name pattern
        // rather than six literals. That the rendered form really has six is asserted in the
        // browser, by PageNavLevelsE2ETests.
        var b = BranchSlice("case \"pagenav\":");
        Assert.Contains("for (var lvl = 1; lvl <= 6; lvl++)", b);
        Assert.Contains("name=\"props[@lvlKey]\"", b);
        Assert.Contains("$\"h{lvl}\"", b);
    }

    [Fact]
    public void PageNav_HeadingLevelBoxes_AreCheckboxes()
    {
        Assert.Contains("type=\"checkbox\" value=\"true\"", BranchSlice("case \"pagenav\":"));
    }

    [Fact]
    public void PageNav_AWidgetPlacedBeforeTheBoxesExisted_ShowsH2AndH3Ticked()
    {
        // Not the same as an author unticking everything, and the editor has to render the
        // difference or opening an old widget would look like it had been turned off.
        Assert.Contains("lvl is 2 or 3", BranchSlice("case \"pagenav\":"));
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
