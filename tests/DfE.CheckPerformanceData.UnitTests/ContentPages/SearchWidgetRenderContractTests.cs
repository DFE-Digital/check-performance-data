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
