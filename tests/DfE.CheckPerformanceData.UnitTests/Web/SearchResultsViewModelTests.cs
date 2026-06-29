using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class SearchResultsViewModelTests
{
    [Theory]
    [InlineData(25, 20, 2)]   // rounds up
    [InlineData(20, 20, 1)]   // exact fit
    [InlineData(21, 20, 2)]
    [InlineData(0, 20, 0)]    // no results
    public void TotalPages_RoundsUp(int totalCount, int pageSize, int expected)
    {
        var vm = new SearchResultsViewModel { TotalCount = totalCount, PageSize = pageSize };

        Assert.Equal(expected, vm.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenPageSizeIsZero_ReturnsZero_WithoutDivideByZero()
    {
        var vm = new SearchResultsViewModel { TotalCount = 5, PageSize = 0 };

        Assert.Equal(0, vm.TotalPages);
    }

    [Fact]
    public void HasAnyResults_IsTrue_WhenOnlyContentBlockResultsPresent()
    {
        // The new guidance-search behaviour: wiki returns nothing, content blocks match.
        var vm = new SearchResultsViewModel
        {
            ContentResults = [ContentHit()]
        };

        Assert.True(vm.HasAnyResults);
    }

    [Fact]
    public void HasAnyResults_IsFalse_WhenBothListsEmpty()
    {
        var vm = new SearchResultsViewModel();

        Assert.False(vm.HasAnyResults);
    }

    private static ContentBlockSearchResultDto ContentHit() => new()
    {
        Key = "guidance-ks4-2026-key-dates",
        Url = "/guidance/2026-ks4-june-checking-exercise#key-dates",
        PageTitle = "How to check your performance measures data: KS4 June 2026",
        SnippetHtml = "<mark>key</mark> dates"
    };
}
