using System.Text.RegularExpressions;
using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// End-to-end coverage for the search widget's instant-search option, in both the
// "this page" and "a section of the site" flavours. Each test seeds its own pages
// under /help with Guid-suffixed segments so runs can interleave; teardown
// best-effort deletes them.
//
// The load-bearing claim these tests defend is that instant search is an
// enhancement: every combination still renders a working GET form, and the
// no-JavaScript test proves it by turning JavaScript off.
//
// Pairs with:
//   SearchWidgetRenderContractTests (source-file tokens)
//   SearchSuggestionsControllerTests (endpoint input guards)
//   SiteSearchSuggestTests (service behaviour against real Postgres)
//   WidgetEditorContractTests (editor form fields)
[Collection("E2E")]
public sealed class InstantSearchWidgetE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        // Children first: the CMS refuses to delete a page that still has descendants.
        for (var i = _createdPages.Count - 1; i >= 0; i--)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _createdPages[i]);
        }
        await base.DisposeAsync();
    }

    // Seeds a content page carrying a search widget followed by two headings, each with a
    // paragraph under it. The second paragraph carries a unique token so a body-only match
    // can be told apart from a heading match.
    private async Task<(string UrlPath, string BodyToken)> SeedPageWithSectionsAsync(
        Dictionary<string, string> searchProps)
    {
        var segment = $"e2e-instant-{Guid.NewGuid():N}";
        var bodyToken = "cypdbody" + Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();

        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient,
            parentId: CmsSeedHelpers.HelpRootId,
            pageType: "content",
            segment: segment,
            title: "E2E instant search");
        _createdPages.Add(id);

        await AddAndSetAsync(id, "0.0", "search", searchProps);
        await AddAndSetAsync(id, "0.1", "heading",
            new Dictionary<string, string> { ["level"] = "2", ["text"] = "Providing evidence" });
        await AddAndSetAsync(id, "0.2", "richtext",
            new Dictionary<string, string> { ["html"] = "<p>Attach a document showing the correction.</p>" });
        await AddAndSetAsync(id, "0.3", "heading",
            new Dictionary<string, string> { ["level"] = "2", ["text"] = "Uploading files" });
        await AddAndSetAsync(id, "0.4", "richtext",
            new Dictionary<string, string> { ["html"] = $"<p>Accepted formats include {bodyToken} archives.</p>" });

        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return ($"/help/{segment}", bodyToken);
    }

    private async Task AddAndSetAsync(Guid pageId, string path, string widgetType, Dictionary<string, string> props)
    {
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, pageId, path, widgetType);
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, pageId, path, widgetType, props);
    }

    private static Dictionary<string, string> SearchProps(
        string searchIn, bool instant, string scope = "", string noResults = "Nothing on this page")
    {
        var props = new Dictionary<string, string>
        {
            ["label"] = "Search this page",
            ["action"] = "/search",
            ["placeholder"] = "",
            ["buttonText"] = "Search",
            ["scope"] = scope,
            ["searchIn"] = searchIn,
            ["noResultsText"] = noResults,
        };
        // An unticked checkbox posts no field at all, which is how the widget reads "off".
        if (instant) props["instant"] = "true";
        return props;
    }

    private ILocator Menu => Page.Locator("ul.autocomplete__menu");
    private ILocator Options => Page.Locator("li.autocomplete__option");

    private async Task TypeAsync(string text)
    {
        var input = Page.Locator("input.autocomplete__input");
        await input.ClickAsync();
        await input.FillAsync(text);
    }

    // ============================================================
    // 1. This page: suggestions are the page's own sections, and choosing one moves
    //    both the URL and the reading position to that heading.
    // ============================================================
    [Fact]
    public async Task PageMode_SuggestsSections_AndChoosingOneJumpsAndMovesFocus()
    {
        var (url, _) = await SeedPageWithSectionsAsync(SearchProps("page", instant: true));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator("input.autocomplete__input")).ToBeVisibleAsync();

        await TypeAsync("evidence");
        await Expect(Options.First).ToBeVisibleAsync();
        await Expect(Options.First).ToContainTextAsync("Providing evidence");

        var anchor = await Page.Locator("h2:has-text('Providing evidence')").First.GetAttributeAsync("id");
        Assert.False(string.IsNullOrEmpty(anchor));

        await Options.First.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"#{Regex.Escape(anchor!)}$"));
        var focusedId = await Page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id");
        Assert.Equal(anchor, focusedId);
    }

    // ============================================================
    // 2. This page: a word that appears only in body copy still suggests the heading
    //    of the section it sits in, because that is the only place to land.
    // ============================================================
    [Fact]
    public async Task PageMode_BodyOnlyMatch_SuggestsItsEnclosingHeading()
    {
        var (url, bodyToken) = await SeedPageWithSectionsAsync(SearchProps("page", instant: true));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await TypeAsync(bodyToken);

        await Expect(Options.First).ToBeVisibleAsync();
        await Expect(Options.First).ToContainTextAsync("Uploading files");
    }

    // ============================================================
    // 3. This page: nothing matches — the author's copy is what the visitor reads.
    // ============================================================
    [Fact]
    public async Task PageMode_NoMatch_ShowsTheAuthorsNoResultsText()
    {
        var (url, _) = await SeedPageWithSectionsAsync(
            SearchProps("page", instant: true, noResults: "Nothing on this page"));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await TypeAsync("zzzqqqnothinghere");

        await Expect(Menu).ToContainTextAsync("Nothing on this page");
    }

    // ============================================================
    // 4. A section of the site: suggestions are documents under the configured path,
    //    nothing outside it, and choosing one opens that page.
    // ============================================================
    [Fact]
    public async Task PathMode_SuggestsDocumentsUnderThePath_AndChoosingOneNavigates()
    {
        var token = "cypdscope" + Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();

        var containerSegment = $"e2e-section-{Guid.NewGuid():N}";
        var containerId = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, CmsSeedHelpers.HelpRootId, "content", containerSegment, "E2E section");
        _createdPages.Add(containerId);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, containerId);

        var insideSegment = $"e2e-inside-{Guid.NewGuid():N}";
        var insideId = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, containerId, "content", insideSegment, $"Inside {token} page");
        _createdPages.Add(insideId);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, insideId);

        var outsideSegment = $"e2e-outside-{Guid.NewGuid():N}";
        var outsideId = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, CmsSeedHelpers.HelpRootId, "content", outsideSegment, $"Outside {token} page");
        _createdPages.Add(outsideId);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, outsideId);

        var (hostUrl, _) = await SeedPageWithSectionsAsync(
            SearchProps("path", instant: true, scope: $"help/{containerSegment}"));

        await Page.GotoAsync($"{Fixture.BaseUrl}{hostUrl}");
        await TypeAsync(token);

        // The remote source debounces, so the menu shows its empty state for a moment before
        // the first response lands. Waiting on the text, not on any option, skips that frame.
        await Expect(Options.First).ToContainTextAsync("Inside");
        var labels = await Options.AllInnerTextsAsync();
        Assert.DoesNotContain(labels, l => l.Contains("Outside", StringComparison.Ordinal));

        await Options.First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/help/{Regex.Escape(containerSegment)}/{Regex.Escape(insideSegment)}$"));
    }

    // ============================================================
    // 5. This page, instant off: the button submits a search scoped to this page, which
    //    is also where a visitor without JavaScript ends up.
    // ============================================================
    [Fact]
    public async Task PageMode_WithoutInstant_SubmitsASearchScopedToThisPage()
    {
        var (url, _) = await SeedPageWithSectionsAsync(SearchProps("page", instant: false));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        // No enhancement: the plain input is still the one in the markup.
        Assert.Equal(0, await Page.Locator("input.autocomplete__input").CountAsync());

        await Page.Locator("input[name='q']").FillAsync("evidence");
        await Page.Locator("button[type='submit']").ClickAsync();

        // The scope travels URL-encoded, so the slash in the page path arrives as %2F.
        var encodedScope = Uri.EscapeDataString(url.TrimStart('/'));
        await Expect(Page).ToHaveURLAsync(new Regex($@"/search\?.*scope={Regex.Escape(encodedScope)}"));
    }

    // ============================================================
    // 6. No JavaScript: the form is still there and still works, in the flavour that
    //    depends on JavaScript the most.
    // ============================================================
    [Fact]
    public async Task WithoutJavaScript_ThePlainFormStillRendersAndSubmits()
    {
        var (url, _) = await SeedPageWithSectionsAsync(SearchProps("page", instant: true));

        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
        });
        var impersonation = TestHttpClients.ImpersonationCookieHeader;
        if (!string.IsNullOrEmpty(impersonation))
        {
            var equalsIndex = impersonation.IndexOf('=');
            await context.AddCookiesAsync([new Cookie
            {
                Name = impersonation[..equalsIndex],
                Value = impersonation[(equalsIndex + 1)..],
                Url = Fixture.BaseUrl,
            }]);
        }

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await page.Locator("input[name='q']").FillAsync("evidence");
        await page.Locator("button[type='submit']").ClickAsync();

        await page.WaitForURLAsync(new Regex(@"/search\?"));
        Assert.Contains("scope=", page.Url, StringComparison.Ordinal);
    }

    // ============================================================
    // 7. The enhanced widget carries no accessibility violations.
    // ============================================================
    [Fact]
    public async Task EnhancedWidget_HasNoAxeViolations()
    {
        var (url, _) = await SeedPageWithSectionsAsync(SearchProps("page", instant: true));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator("input.autocomplete__input")).ToBeVisibleAsync();
        await TypeAsync("evidence");
        await Expect(Options.First).ToBeVisibleAsync();

        var results = await Page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa", "wcag21a", "wcag21aa" },
            },
        });

        Assert.True(results.Violations.Length == 0, Describe(results.Violations));
    }

    private static string Describe(AxeResultItem[] violations)
    {
        var sb = new StringBuilder();
        sb.Append(violations.Length).AppendLine(" axe violation(s) on the instant-search widget:");
        foreach (var v in violations)
        {
            sb.Append("  - ").Append(v.Id)
              .Append(" (impact: ").Append(v.Impact ?? "unknown")
              .Append(", nodes: ").Append(v.Nodes.Length).Append(')').AppendLine();
        }
        return sb.ToString();
    }
}
