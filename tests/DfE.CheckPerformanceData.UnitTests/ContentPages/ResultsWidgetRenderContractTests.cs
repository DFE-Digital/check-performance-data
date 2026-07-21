namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Static-Razor contract for the search-results widget. Behaviour tests that would
// require a full MVC render harness are covered instead by asserting on tokens in the
// _Results.cshtml source. Pairs with SiteSearchMergedPagedTests, which pins the
// service-side math.
public sealed class ResultsWidgetRenderContractTests
{
    private static readonly string View = ReadResultsView();

    // ----- Rendered output -----

    [Fact]
    public void DoesNotRenderSectionHeaders_ForPagesOrContentBlocks()
    {
        // Manual step 6-8: one flat list, reader shouldn't see "Pages (N)" or "Content blocks (N)".
        Assert.DoesNotContain("Pages (", View);
        Assert.DoesNotContain("Content blocks (", View);
    }

    [Fact]
    public void RendersMergedItems_ViaSiteSearchPagedResult()
    {
        // Uses the merged/paged service surface (not the split result).
        Assert.Contains("SearchMergedPagedAsync", View);
        Assert.Contains("result.Items", View);
    }

    [Fact]
    public void RendersEmptyState_UsingWidgetConfiguredText()
    {
        // Manual step 12: empty state uses the widget's `emptyText` prop.
        Assert.Contains("Model.GetString(\"emptyText\")", View);
        Assert.Contains("@emptyText", View);
    }

    [Fact]
    public void RendersEmptyQueryPrompt()
    {
        // Manual step 5: with no ?q= we prompt "Enter a search term to begin.".
        Assert.Contains("Enter a search term to begin.", View);
    }

    [Fact]
    public void RendersBelowMinimumLengthMessage()
    {
        // Manual step 11: q shorter than the minimum shows the below-min prompt.
        Assert.Contains("SearchInvalidReason.BelowMinimumLength", View);
        Assert.Contains("Enter at least 2 characters.", View);
    }

    // ----- Pagination -----

    [Fact]
    public void RendersGovukPagination_OnlyWhenMoreThanOnePage()
    {
        // Manual step 7-8: pagination component appears only when totalPages > 1.
        Assert.Contains("<govuk-pagination>", View);
        Assert.Contains("result.TotalPages > 1", View);
    }

    [Fact]
    public void PreviousLink_HiddenOnFirstPage()
    {
        Assert.Contains("<govuk-pagination-previous", View);
        Assert.Contains("pageOneIx > 1", View);
    }

    [Fact]
    public void NextLink_HiddenOnLastPage()
    {
        Assert.Contains("<govuk-pagination-next", View);
        Assert.Contains("pageOneIx < result.TotalPages", View);
    }

    [Fact]
    public void PaginationUrls_PreserveQAndScope_AndSetPage()
    {
        // Manual step 7: paging URLs must carry q=, scope=, and page= for a stable back-nav.
        Assert.Matches("parts\\.Add.*q=", View);
        Assert.Matches("parts\\.Add.*scope=", View);
        Assert.Matches("parts\\.Add.*page=", View);
    }

    [Fact]
    public void PageParam_UsesOneIndexedUrls()
    {
        // Manual step 7: ?page=1 = first page (matches other paged admin views).
        // First page (URL 1) should not add page= to the URL (parts kept minimal).
        Assert.Contains("oneIndexed > 1", View);
    }

    // ----- Setting-driven page size -----

    [Fact]
    public void PageSize_ComesFromWikiPageLengthSetting()
    {
        // Manual step 13-15: results per page follows the admin setting, not a widget prop.
        Assert.Contains("SettingKeys.WikiPageLength", View);
        Assert.Contains("SettingService.GetIntAsync", View);
    }

    [Fact]
    public void PageSize_HasSafeFallbackWhenSettingIsInvalid()
    {
        // Widget must not divide by zero if the setting is 0/negative.
        Assert.Matches("pageSize\\s*<\\s*1", View);
    }

    // ----- Scope precedence -----

    [Fact]
    public void UrlScope_WinsOverWidgetScope()
    {
        // Manual step 19: ?scope=x on the URL beats the widget-configured scope.
        Assert.Contains("widgetScope", View);
        Assert.Contains("urlScope", View);
        Assert.Contains("effectiveScope", View);
        // The 'effective' assignment picks urlScope first, widgetScope only if urlScope empty.
        Assert.Matches("!string\\.IsNullOrEmpty\\(urlScope\\)\\s*\\?\\s*urlScope\\s*:\\s*widgetScope", View);
    }

    // ----- Injected services -----

    [Fact]
    public void Injects_SiteSearchService_AndSettingService()
    {
        Assert.Contains("@inject ISiteSearchService", View);
        Assert.Contains("@inject ISettingService", View);
    }

    // ----- helpers -----

    private static string ReadResultsView()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var path = Path.Combine(
            solutionRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views",
            "Shared", "ContentPages", "Widgets", "_Results.cshtml");
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
