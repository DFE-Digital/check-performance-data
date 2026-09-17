using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// The dashboard's read layer is thirty-odd hand-written SQL statements. Every one of them has
// to go through the surface-filtered source, because a query that named search_events directly
// would quietly report across every surface while the page around it claimed to be filtered.
// That is not something a reviewer can be relied on to catch, so it is asserted here.
public sealed class SearchAnalyticsSurfaceSourceTests
{
    private static readonly string Source = ReadQueryService();

    [Fact]
    public void NoQueryNamesTheEventsTableDirectly()
    {
        // The one permitted mention is inside EventsSource itself.
        var matches = Regex.Matches(Source, @"(FROM|JOIN)\s+search_events\b");

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one direct reference to search_events (inside EventsSource); found {matches.Count}. "
            + "A new query must read FROM {EventsSource} so the surface filter applies to it.");
    }

    [Fact]
    public void TheEventsSourceFiltersOnSurface()
    {
        Assert.Contains("surface = ANY(@surfaces)", Source);
    }

    [Fact]
    public void TheSurfaceParameterIsBoundInOnePlace()
    {
        // Bound centrally in ReadAsync; a per-call-site binding is how one query ends up
        // missing it.
        var bindings = Regex.Matches(Source, @"NpgsqlParameter\(""surfaces""");

        Assert.Single(bindings);
    }

    private static string ReadQueryService()
    {
        var root = FindSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(
            root, "src", "DfE.CheckPerformanceData.Persistence", "Analytics",
            "SearchAnalyticsQueryService.cs"));
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
