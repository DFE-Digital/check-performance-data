namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Static-Razor contract for the search-input widget (_Search.cshtml). Pairs with
// SiteSearchServiceTests + the results-widget contract tests to prove the
// input->results hop stays wired.
public sealed class SearchWidgetRenderContractTests
{
    private static readonly string View = ReadSearchView();

    [Fact]
    public void SubmitsAsGetForm_WithConfiguredAction()
    {
        Assert.Contains("method=\"get\"", View);
        Assert.Contains("action=\"@action\"", View);
    }

    [Fact]
    public void EmitsNameQ_ForQueryInput()
    {
        Assert.Contains("name=\"q\"", View);
    }

    [Fact]
    public void EmitsHiddenScopeInput_OnlyWhenScopeSet()
    {
        // Manual step 23: submitting with a widget scope adds ?scope= to the URL.
        Assert.Contains("type=\"hidden\"", View);
        Assert.Contains("name=\"scope\"", View);
        Assert.Contains("if (!string.IsNullOrEmpty(scope))", View);
    }

    // Pages stored by id travel on the form as their short tokens (?pages=), so the search URL
    // stays short however long the pages' paths are.
    [Fact]
    public void EmitsHiddenPagesInput_FromTheStoredPageIds()
    {
        Assert.Contains("ScopePageIds.ToPageTokens(Model.GetString(\"scopePageIds\"))", View);
        Assert.Contains("<input type=\"hidden\" name=\"pages\" value=\"@pages\" />", View);
        Assert.Contains("data-pages=\\\"{enc.Encode(pages)}\\\"", View);
    }

    [Fact]
    public void ReadsScopeFromProps_AndNormalisesSlashes()
    {
        // Widget's `scope` prop is trimmed and stripped of surrounding slashes so
        // "help" and "/help/" resolve to the same value.
        Assert.Contains("GetString(\"scope\")", View);
        Assert.Contains("SearchScope.Normalise(", View);
    }

    // ----- Search target: whole site / a section / this page -----

    [Fact]
    public void ReadsTheSearchTargetFromProps()
    {
        Assert.Contains("GetString(\"searchIn\")", View);
    }

    [Fact]
    public void AbsentSearchTargetFallsBackToScope_SoExistingWidgetsKeepWorking()
    {
        // Widgets placed before searchIn existed carry only a scope. A set scope has to keep
        // meaning "a section of the site", and an empty one the whole site, or every widget
        // already in the CMS would silently change behaviour.
        var view = View;
        var fallback = view.IndexOf("searchIn.Length == 0", StringComparison.Ordinal);
        Assert.True(fallback >= 0, "Search view has no back-compat fallback for an absent searchIn.");

        var line = view.Substring(fallback, view.IndexOf('\n', fallback) - fallback);
        Assert.Contains("\"path\"", line);
        Assert.Contains("\"site\"", line);
    }

    [Fact]
    public void AnUnrecognisedSearchTargetDegradesToWholeSite()
    {
        Assert.Contains("searchIn = \"site\"", View);
    }

    [Fact]
    public void PageTargetScopesToTheCurrentRequestPath()
    {
        // "This page" with JavaScript off submits to a search of just this page, which is only
        // possible if the form carries the page's own path as its scope.
        Assert.Contains("Request.Path", View);
    }

    // ----- Instant search is an enhancement, never a replacement -----

    [Fact]
    public void QueryInputAndSubmitAreEmittedAheadOfTheInstantBranch()
    {
        // The no-JS contract: the plain GET form exists in every combination of the two axes,
        // ahead of anything instant search adds.
        var view = View;
        var instantBranch = view.IndexOf("@if (instant)", StringComparison.Ordinal);
        Assert.True(instantBranch >= 0, "Search view has no instant-search branch.");

        var queryInput = view.IndexOf("name=\"q\"", StringComparison.Ordinal);
        var submit = view.IndexOf("type=\"submit\"", StringComparison.Ordinal);

        Assert.InRange(queryInput, 0, instantBranch);
        Assert.InRange(submit, 0, instantBranch);
    }

    [Fact]
    public void InstantSearchMarksTheFormAndCarriesItsConfiguration()
    {
        var view = View;
        Assert.Contains("data-cypmd-instant-search", view);
        Assert.Contains("data-search-in", view);
        Assert.Contains("data-scope", view);
        Assert.Contains("data-no-results", view);
    }

    [Fact]
    public void InstantSearchAttributesAreOmittedWhenItIsOff()
    {
        // The marker is written only when instant search is on; the rendered output is checked in
        // the view render tests.
        Assert.Contains("var instantAttributes = instant", View);
    }

    [Fact]
    public void InstantSearchLoadsItsScript_OnlyInsideTheInstantBranch()
    {
        var view = View;
        var instantBranch = view.IndexOf("@if (instant)", StringComparison.Ordinal);
        var script = view.IndexOf("instant-search.js", StringComparison.Ordinal);

        Assert.True(script > instantBranch, "instant-search.js must load only when instant search is on.");
    }

    [Fact]
    public void ReadsTheInstantFlagAsABoolean()
    {
        // Stored as the string "true"/"false" by the form post, same as pagenav's showSearch.
        Assert.Contains("GetBool(\"instant\")", View);
    }

    [Fact]
    public void EachWidgetOnAPageGetsItsOwnInputId()
    {
        // The label's `for` and the ids accessible-autocomplete derives from the input both key
        // off this, so a second search widget sharing the id would aim a screen reader at the
        // first widget's menu.
        Assert.Contains("cypmd-search-widget-seq", View);
    }

    // ----- Optional search button -----

    // Instant search can drop the button; without instant search it is always shown, and a widget
    // with no stored value (saved before the option existed) keeps it.
    [Fact]
    public void ShowsTheButtonUnlessInstantSearchTurnsItOff()
    {
        Assert.Contains("var showButton = !instant || (Model.GetBool(\"showButton\") ?? true);", View);
    }

    [Fact]
    public void TheSubmitButtonIsRenderedOnlyWhenShown()
    {
        var view = View;
        var guard = view.IndexOf("@if (showButton)", StringComparison.Ordinal);
        var submit = view.IndexOf("type=\"submit\"", StringComparison.Ordinal);

        Assert.True(guard >= 0, "Search view does not guard the button on showButton.");
        Assert.True(submit > guard, "The submit button must sit inside the showButton guard.");
    }

    // Without a button the label and the form's action are what keep the box usable: the label
    // still names the input, and Enter submits the form to its action.
    [Fact]
    public void TheLabelAndActionDoNotDependOnTheButton()
    {
        var view = View;
        var guard = view.IndexOf("@if (showButton)", StringComparison.Ordinal);

        Assert.InRange(view.IndexOf("for=\"@inputId\"", StringComparison.Ordinal), 0, guard);
        Assert.InRange(view.IndexOf("action=\"@action\"", StringComparison.Ordinal), 0, guard);
    }

    // ----- Button below the box -----

    // In a narrow column a button beside the box leaves the box too small to type in. The author
    // can ask for the button underneath instead; a widget with no stored value keeps the button
    // beside the box, where it has always been.
    [Fact]
    public void PutsTheButtonBelowTheBox_OnlyWhenAsked()
    {
        Assert.Contains("var buttonBelow = Model.GetBool(\"buttonBelow\") ?? false;", View);
        Assert.Contains("cypmd-search__row--stacked", View);
    }

    // The stacked layout is a style of the same row: the input still comes before the button in
    // the markup, so the reading and tab order do not change.
    [Fact]
    public void StackingDoesNotReorderTheInputAndTheButton()
    {
        var input = View.IndexOf("name=\"q\"", StringComparison.Ordinal);
        var submit = View.IndexOf("type=\"submit\"", StringComparison.Ordinal);

        Assert.True(input >= 0 && submit > input, "The input must come before the submit button.");
    }

    [Fact]
    public void TheStackedRow_PutsTheButtonOnItsOwnLine()
    {
        var css = File.ReadAllText(Path.Combine(
            FindSolutionRoot(AppContext.BaseDirectory), "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "site.css"));
        var rule = css.IndexOf(".cypmd-search__row--stacked {", StringComparison.Ordinal);

        Assert.True(rule >= 0, "site.css has no rule for the stacked search row.");
        Assert.Contains("flex-direction: column;", css[rule..css.IndexOf('}', rule)]);
    }

    private static string ReadSearchView()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var path = Path.Combine(
            solutionRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views",
            "Shared", "ContentPages", "Widgets", "_Search.cshtml");
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
