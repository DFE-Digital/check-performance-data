using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Regression guard for the site-search results widget: the widget must be
// registered in WidgetRegistry with its full prop shape, the runtime dispatcher
// (_Widget.cshtml) must route "results" to the Results partial, and the editor
// (_EditWidget.cshtml) must expose an editor branch. Assertions are static
// content-file reads to avoid pulling in a full MVC test host.
public sealed class ResultsWidgetRegistrationTests
{
    [Fact]
    public void Registry_HasResultsWidget()
    {
        var def = WidgetRegistry.Get("results");
        Assert.NotNull(def);
        Assert.Equal("Search results", def!.PaletteLabel);
    }

    [Fact]
    public void Registry_ResultsWidget_HasScopeAndEmptyTextOnly()
    {
        // Include-toggles and max-per-type are deliberately absent: the widget renders one
        // merged list (no page/block distinction), and its page size comes from the
        // CMS:PageLength setting rather than a widget prop.
        var props = WidgetRegistry.CreateDefaultProps("results");
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("scope"));
        Assert.True(props.ContainsKey("emptyText"));
        Assert.False(props.ContainsKey("includePages"));
        Assert.False(props.ContainsKey("includeContentBlocks"));
        Assert.False(props.ContainsKey("maxPerType"));
    }

    [Fact]
    public void RuntimeDispatcher_RoutesResultsToResultsPartial()
    {
        var view = ReadView("Shared/ContentPages/_Widget.cshtml");
        Assert.Contains("\"results\"", view);
        Assert.Contains("ContentPages/Widgets/_Results", view);
    }

    [Fact]
    public void EditorHasResultsBranch()
    {
        var view = ReadView("Shared/ContentPages/_EditWidget.cshtml");
        Assert.Contains("case \"results\":", view);
    }

    [Fact]
    public void ResultsPartial_Exists()
    {
        var path = ViewPath("Shared/ContentPages/Widgets/_Results.cshtml");
        Assert.True(File.Exists(path),
            $"Expected {path} to exist — the results widget renders through this partial.");
    }

    private static string ReadView(string relativePath)
        => File.ReadAllText(ViewPath(relativePath));

    private static string ViewPath(string relativePath)
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(
            solutionRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
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
