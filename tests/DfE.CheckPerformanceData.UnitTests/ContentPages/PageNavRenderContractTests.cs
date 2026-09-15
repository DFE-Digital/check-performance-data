namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Static-Razor contract for the page-nav widget. The behaviour worth pinning is that a headings
// nav is built from the page's own tree using this widget's chosen levels — reading the list the
// controller precomputed instead would silently ignore the author's choice, and two nav widgets
// on one page could not differ.
public sealed class PageNavRenderContractTests
{
    private static readonly string View = ReadView();

    [Fact]
    public void HeadingsMode_BuildsFromThePagesOwnTree()
    {
        Assert.Contains("PageContentTree", View);
        Assert.Contains("ContentNavBuilder.Build(tree", View);
    }

    [Fact]
    public void HeadingsMode_UsesTheLevelsThisWidgetChose()
    {
        Assert.Contains("NavLevelSelection.For(Model)", View);
    }

    [Fact]
    public void WithNoTreeInScope_ItFallsBackToThePrecomputedList()
    {
        // The editor preview renders the widget with no page tree around it; falling back keeps
        // it showing something rather than rendering blank.
        Assert.Contains("PageHeadingNav", View);
    }

    private static string ReadView()
    {
        var root = FindSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(
            root, "src", "DfE.CheckPerformanceData.Web", "Views",
            "Shared", "ContentPages", "Widgets", "_PageNav.cshtml"));
    }

    private static string FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetDirectories("src").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate solution root from {startDir}.");
    }
}
