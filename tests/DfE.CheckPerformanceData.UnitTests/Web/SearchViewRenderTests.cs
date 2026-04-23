namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Bodies filled by Plan 07 Task 4 — tests the rendered output of Views/Help/Search.cshtml
// and Views/Help/_WikiSearch.cshtml. The render-harness choice is documented in Plan 07
// Task 4's <action> block (ViewResult+Model shape assertion, with optional HtmlAgilityPack
// on the rendered HTML string if a render harness is introduced at that time).
public sealed class SearchViewRenderTests
{
    [Fact]
    public async Task SearchView_RendersResultTitleLinkWithSlugPath()
    {
        await Task.CompletedTask; // TODO(Plan 07 Task 4): assert <h2><a href="/help/{slugPath}">
    }

    [Fact]
    public async Task SearchView_RendersSlugBreadcrumb()
    {
        await Task.CompletedTask; // TODO(Plan 07 Task 4): assert slug breadcrumb element present
    }

    [Fact]
    public async Task SearchView_RendersMarkInSnippet()
    {
        await Task.CompletedTask; // TODO(Plan 07 Task 4): assert <mark> appears within snippet
    }

    [Fact]
    public async Task SearchView_RendersPaginationWhenTotalCountExceedsPageSize()
    {
        await Task.CompletedTask; // TODO(Plan 07 Task 4): assert <govuk-pagination> when totalCount > pageSize
    }
}
