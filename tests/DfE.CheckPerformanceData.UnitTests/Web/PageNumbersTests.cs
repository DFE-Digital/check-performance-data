using DfE.CheckPerformanceData.Web.Pagination;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The windowed page-number list behind the shared pager control. The view-level concern (which
// page numbers to show, and where a run of hidden pages collapses to an ellipsis) is a pure
// function so it can be exhaustively tested without an MVC host: the partial only renders what
// this returns. Best-practice GDS pagination — first and last always shown, the current page with
// one neighbour either side, an ellipsis for any gap of more than one hidden page, and a single
// hidden page shown rather than elided.
public sealed class PageNumbersTests
{
    // Renders the item list to a compact string: "1", "…" for an ellipsis, "[5]" for the current
    // page, so the windowing shape is asserted at a glance.
    private static string Shape(int current, int total) =>
        string.Join(" ", PageNumbers.Build(current, total).Select(i =>
            i.IsEllipsis ? "…" : i.IsCurrent ? $"[{i.Number}]" : i.Number.ToString()));

    [Fact]
    public void FirstPageOfMany_ShowsTheHead_ThenAnEllipsis_ThenTheLast()
    {
        // The 82-page case from UAT: never 1..82 listed in full.
        Assert.Equal("[1] 2 … 82", Shape(1, 82));
    }

    [Fact]
    public void MiddlePage_ShowsFirst_Window_AndLast_WithEllipsesEitherSide()
    {
        Assert.Equal("1 … 4 [5] 6 … 82", Shape(5, 82));
    }

    [Fact]
    public void NearTheStart_DoesNotEllipsizeASinglePageGap()
    {
        // Page 3: 1 2 [3] 4 then a gap to the last — the gap between 1 and 2 is none.
        Assert.Equal("1 2 [3] 4 … 82", Shape(3, 82));
    }

    [Fact]
    public void NearTheEnd_ShowsTheTail()
    {
        Assert.Equal("1 … 80 [81] 82", Shape(81, 82));
    }

    [Fact]
    public void ASingleHiddenPage_IsShownRatherThanEllipsized()
    {
        // current 4 of 5: wanted {1,3,4,5}; the gap is just page 2, so show it, not an ellipsis.
        Assert.Equal("1 2 3 [4] 5", Shape(4, 5));
    }

    [Fact]
    public void FewPages_AreAllShown_WithNoEllipsis()
    {
        Assert.Equal("1 2 [3] 4 5", Shape(3, 5));
    }

    [Fact]
    public void SinglePage_IsJustThatPage()
    {
        Assert.Equal("[1]", Shape(1, 1));
    }

    [Fact]
    public void ExactlyOneCurrentPage_IsFlagged()
    {
        var items = PageNumbers.Build(5, 82);
        Assert.Single(items, i => i.IsCurrent);
        Assert.Equal(5, items.Single(i => i.IsCurrent).Number);
    }

    [Theory]
    [InlineData(0, 5, 1)]   // below the floor clamps to the first page
    [InlineData(-3, 5, 1)]
    [InlineData(99, 5, 5)]  // above the ceiling clamps to the last page
    public void OutOfRangeCurrent_ClampsIntoRange(int current, int total, int expectedCurrent)
    {
        var items = PageNumbers.Build(current, total);
        Assert.Equal(expectedCurrent, items.Single(i => i.IsCurrent).Number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoPages_ReturnsEmpty(int total)
    {
        Assert.Empty(PageNumbers.Build(1, total));
    }

    [Fact]
    public void EveryNonEllipsisItem_IsAValidPageNumber_InAscendingOrder()
    {
        var numbers = PageNumbers.Build(40, 82)
            .Where(i => !i.IsEllipsis)
            .Select(i => i.Number)
            .ToArray();

        Assert.All(numbers, n => Assert.InRange(n, 1, 82));
        Assert.Equal(numbers.OrderBy(n => n).ToArray(), numbers);
        Assert.Equal(numbers.Distinct().ToArray(), numbers); // no duplicates
    }
}
