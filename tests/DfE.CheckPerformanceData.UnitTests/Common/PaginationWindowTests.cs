using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public class PaginationWindowTests
{
    // Renders the window the way a user sees it: 1-based page numbers, "..." for an ellipsis.
    private static string Render(int currentPage, int totalPages) =>
        string.Join(" ", PaginationWindow.Build(currentPage, totalPages)
            .Select(p => p is null ? "..." : (p.Value + 1).ToString()));

    [Theory]
    // Short ranges stay complete — nothing to collapse.
    [InlineData(0, 1, "1")]
    [InlineData(0, 3, "1 2 3")]
    [InlineData(1, 3, "1 2 3")]
    [InlineData(2, 5, "1 2 3 4 5")]
    // A single skipped page is shown rather than collapsed: "1 ... 3" saves nothing over "1 2 3".
    [InlineData(0, 4, "1 2 3 4")]
    // Long ranges collapse around the current page.
    [InlineData(0, 20, "1 2 ... 20")]
    [InlineData(1, 20, "1 2 3 ... 20")]
    [InlineData(9, 20, "1 ... 9 10 11 ... 20")]
    [InlineData(18, 20, "1 ... 18 19 20")]
    [InlineData(19, 20, "1 ... 19 20")]
    public void Build_ShowsFirstLastAndTheCurrentNeighbourhood(int current, int total, string expected)
    {
        Assert.Equal(expected, Render(current, total));
    }

    [Fact]
    public void Build_NeverExceedsSevenEntries_HoweverManyPages()
    {
        // First, ellipsis, current-1, current, current+1, ellipsis, last.
        for (var current = 0; current < 500; current++)
        {
            Assert.True(PaginationWindow.Build(current, 500).Count <= 7);
        }
    }

    [Fact]
    public void Build_AlwaysIncludesTheCurrentPage()
    {
        for (var current = 0; current < 200; current++)
        {
            Assert.Contains(current, PaginationWindow.Build(current, 200));
        }
    }

    [Fact]
    public void Build_AlwaysIncludesFirstAndLastPage()
    {
        for (var current = 0; current < 50; current++)
        {
            var window = PaginationWindow.Build(current, 50);
            Assert.Contains(0, window);
            Assert.Contains(49, window);
        }
    }

    [Fact]
    public void Build_ReturnsPagesInAscendingOrderWithNoDuplicates()
    {
        var pages = PaginationWindow.Build(9, 20).Where(p => p is not null).Select(p => p!.Value).ToList();
        Assert.Equal(pages.OrderBy(p => p).ToList(), pages);
        Assert.Equal(pages.Distinct().Count(), pages.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Build_WithNoPages_ReturnsNothing(int totalPages)
    {
        Assert.Empty(PaginationWindow.Build(0, totalPages));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void Build_WithOutOfRangeCurrentPage_ClampsIntoRange(int current)
    {
        // Defensive: ?page=999 in the query string must not produce a broken control.
        var window = PaginationWindow.Build(current, 10);
        Assert.All(window.Where(p => p is not null), p => Assert.InRange(p!.Value, 0, 9));
        Assert.NotEmpty(window);
    }
}
