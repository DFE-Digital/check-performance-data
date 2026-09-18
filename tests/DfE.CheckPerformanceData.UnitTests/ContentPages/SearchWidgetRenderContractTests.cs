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

    [Fact]
    public void ReadsScopeFromProps_AndNormalisesSlashes()
    {
        // Widget's `scope` prop is trimmed and stripped of surrounding slashes so
        // "help" and "/help/" resolve to the same value.
        Assert.Contains("GetString(\"scope\")", View);
        Assert.Contains(".Trim().Trim('/')", View);
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
    public void QueryInputAndSubmitAreEmittedUnconditionally()
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
        // Razor drops an attribute whose value expression is null, so the marker never appears
        // on a non-instant widget and the script never binds to it.
        Assert.Contains("instant ?", View);
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
