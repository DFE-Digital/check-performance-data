namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Bodies filled by Plan 07 Task 4 — tests the static Razor shape of Views/Help/Search.cshtml
// and Views/Help/_WikiSearch.cshtml. The harness choice is source-content assertion via
// File.ReadAllText + Assert.Contains: the behaviours required by SEARCH-02 (title H2 link
// format, slug breadcrumb, <mark> passthrough via @Html.Raw, <govuk-pagination> when
// TotalPages > 1) are all STATIC Razor-source facts. Semantic behaviour is covered by
// HelpControllerSearchTests (Plan 06) and by the WikiRepository.SearchAsync integration
// tests (Plan 04, e.g. Snippet_WrapsMatchesWithMark). No Razor render harness is
// introduced — no in-process MVC test host, no test-server, no new NuGet packages.
public sealed class SearchViewRenderTests
{
    private static string ReadView(string fileName)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web", "Views", "Help"));
        return File.ReadAllText(Path.Combine(viewsDir, fileName));
    }

    [Fact]
    public Task SearchView_RendersResultTitleLinkWithSlugPath()
    {
        var view = ReadView("Search.cshtml");
        // Contract: each result title renders as <h2> with an <a> linking to /help/{slugPath}.
        Assert.Contains("<h2", view);
        Assert.Contains("/help/@r.SlugPath", view);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchView_RendersSlugBreadcrumb()
    {
        var view = ReadView("Search.cshtml");
        // Contract: slug-breadcrumb element appears beneath the title; per 05-UI-SPEC.md
        // §Results Item Anatomy, the breadcrumb renders as "/help/@r.SlugPath" inside a
        // <p class="govuk-body-s">.
        Assert.Contains("govuk-body-s", view);
        Assert.Contains("/help/@r.SlugPath", view);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchView_RendersMarkInSnippet()
    {
        var view = ReadView("Search.cshtml");
        // Contract: the snippet renders via @Html.Raw(r.SnippetHtml) — ts_headline wraps
        // matched terms in <mark>...</mark>. The snippet expression is present; the actual
        // <mark> literal comes from Postgres at runtime (Plan 04 integration test
        // Snippet_WrapsMatchesWithMark).
        Assert.Contains("@Html.Raw(r.SnippetHtml)", view);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchView_RendersPaginationWhenTotalCountExceedsPageSize()
    {
        var view = ReadView("Search.cshtml");
        // Contract: <govuk-pagination> appears inside an @if (Model.TotalPages > 1) block.
        Assert.Contains("Model.TotalPages > 1", view);
        Assert.Contains("<govuk-pagination", view);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchView_RendersBackToHelpLink()
    {
        var view = ReadView("Search.cshtml");
        // Contract: GOV.UK Design System back-link component with href to /help so users
        // can return to the help index from any search state.
        Assert.Contains("govuk-back-link", view);
        Assert.Contains("href=\"/help\"", view);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SearchView_UsesWikiLayoutWithSidebarAndTree()
    {
        var view = ReadView("Search.cshtml");
        // Contract: Search page reuses the wiki-layout + sidebar pattern from Help/Index so
        // navigation tree and search box stay visible alongside results.
        Assert.Contains("wiki-layout", view);
        Assert.Contains("wiki-sidebar", view);
        Assert.Contains("_WikiTree", view);
        Assert.Contains("_WikiSearch", view);
        return Task.CompletedTask;
    }
}
