using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// Every widget an author can place has a test page in /development-testing/widgets that shows it
// set up in several ways: "Heading test page", "Search test page" and so on. The pages are shipped
// with the service and put back as shipped on every start, so they are the same wherever this
// suite runs, and these tests can say exactly what each one should look like.
//
// The tests read the pages as a visitor does. What an author sees in the editor is covered by the
// widget editor tests; what is pinned here is the page each set of options produces.
[Collection("E2E")]
public sealed class WidgetTestPagesE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private ILocator Content => Page.Locator(".cpb-content");

    private ILocator SearchForms => Content.Locator("form.cypmd-search");

    private Task OpenAsync(string segment, string query = "") =>
        Page.GotoAsync($"{Fixture.BaseUrl}{FixtureContent.WidgetsPath}/{segment}{query}");

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.NotNull(box);
        return box;
    }

    // ---- Heading ---------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "govuk-heading-xl")]
    [InlineData(2, "govuk-heading-l")]
    [InlineData(3, "govuk-heading-m")]
    [InlineData(4, "govuk-heading-s")]
    [InlineData(5, "govuk-heading-s")]
    [InlineData(6, "govuk-heading-s")]
    public async Task Heading_EachLevel_IsItsOwnElement_InTheMatchingSize(int level, string size)
    {
        await OpenAsync("heading-test-page");

        var heading = Content.Locator($"h{level}", new() { HasTextString = $"Level {level} heading" });

        await Expect(heading).ToHaveCountAsync(1);
        await Expect(heading).ToHaveClassAsync(new Regex(size));
    }

    // Two level 1 headings on a page is one too many, so a page that brings its own does not get
    // its title as well.
    [Fact]
    public async Task Heading_ALevel1Heading_TakesThePlaceOfThePageTitle()
    {
        await OpenAsync("heading-test-page");

        await Expect(Page.Locator("h1")).ToHaveCountAsync(1);
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Level 1 heading");
    }

    [Fact]
    public async Task Heading_TheSameWordsTwice_GetTwoAnchors()
    {
        await OpenAsync("heading-test-page");

        await Expect(Content.Locator("#repeated-heading")).ToHaveTextAsync("Repeated heading");
        await Expect(Content.Locator("#repeated-heading-2")).ToHaveTextAsync("Repeated heading");
    }

    [Fact]
    public async Task Heading_AnAnchor_IsTheHeadingWithoutItsPunctuation()
    {
        await OpenAsync("heading-test-page");

        await Expect(Content.Locator("#whats-new-in-2026-key-stage-4-16-to-18"))
            .ToHaveTextAsync("What's new in 2026: Key stage 4 & 16 to 18?");
    }

    [Fact]
    public async Task Heading_ALinkToAnAnchor_LandsOnThatHeading()
    {
        await OpenAsync("heading-test-page", "#repeated-heading-2");

        await Expect(Content.Locator("#repeated-heading-2")).ToBeInViewportAsync();
    }

    // ---- Rich text -------------------------------------------------------------------------

    [Fact]
    public async Task RichText_KeepsEmphasisListsTablesAndQuotations()
    {
        await OpenAsync("rich-text-test-page");

        await Expect(Content.Locator("strong", new() { HasTextString = "bold text" })).ToBeVisibleAsync();
        await Expect(Content.Locator("em", new() { HasTextString = "italic text" })).ToBeVisibleAsync();
        await Expect(Content.Locator("code")).ToHaveTextAsync("code");
        await Expect(Content.Locator("ol > li")).ToHaveTextAsync(["first step", "second step", "third step"]);
        await Expect(Content.Locator("ul > li")).ToHaveCountAsync(5);
        await Expect(Content.Locator("table th")).ToHaveTextAsync(["Measure", "2025", "2026"]);
        await Expect(Content.Locator("table tbody tr")).ToHaveCountAsync(2);
        await Expect(Content.Locator("blockquote")).ToContainTextAsync("A quotation, set apart");
    }

    [Fact]
    public async Task RichText_KeepsLinksToPagesWebsitesAndSections()
    {
        await OpenAsync("rich-text-test-page");

        await Expect(Content.GetByRole(AriaRole.Link, new() { Name = "another page in the service" }))
            .ToHaveAttributeAsync("href", FixtureContent.ShortPagePath);
        await Expect(Content.GetByRole(AriaRole.Link, new() { Name = "another website" }))
            .ToHaveAttributeAsync("href", "https://www.gov.uk/");

        await Content.GetByRole(AriaRole.Link, new() { Name = "a section of this page" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("#lists$"));
        await Expect(Content.Locator("#lists")).ToBeInViewportAsync();
    }

    // Only a Heading widget is a section: it has an anchor, and Page navigation lists it.
    [Fact]
    public async Task RichText_AHeadingTypedIntoTheText_HasNoAnchor()
    {
        await OpenAsync("rich-text-test-page");

        var typed = Content.Locator("h3", new() { HasTextString = "A heading typed into the rich text" });

        await Expect(typed).ToBeVisibleAsync();
        Assert.True(string.IsNullOrEmpty(await typed.GetAttributeAsync("id")));
    }

    [Fact]
    public async Task RichText_AnEmptyWidget_TakesUpNoRoom()
    {
        await OpenAsync("rich-text-test-page");

        var before = await BoxAsync(Content.Locator("p", new() { HasTextString = "There is an empty Rich text widget" }));
        var after = await BoxAsync(Content.Locator("p", new() { HasTextString = "This is the next paragraph." }));
        var first = await BoxAsync(Content.Locator("p", new() { HasTextString = "An ordinary paragraph" }));
        var second = await BoxAsync(Content.Locator("p", new() { HasTextString = "A second paragraph" }));

        var gapAroundTheEmptyWidget = after.Y - (before.Y + before.Height);
        var gapBetweenTwoParagraphs = second.Y - (first.Y + first.Height);
        Assert.True(gapAroundTheEmptyWidget <= gapBetweenTwoParagraphs + 1,
            $"the empty widget leaves a gap of {gapAroundTheEmptyWidget}; two paragraphs are {gapBetweenTwoParagraphs} apart");
    }

    // ---- Divider ---------------------------------------------------------------------------

    [Fact]
    public async Task Divider_IsALine_HiddenFromScreenReaders()
    {
        await OpenAsync("divider-test-page");

        var dividers = Content.Locator("hr.govuk-section-break--visible");

        await Expect(dividers).ToHaveCountAsync(7);
        foreach (var divider in await dividers.AllAsync())
            await Expect(divider).ToHaveAttributeAsync("aria-hidden", "true");
    }

    [Fact]
    public async Task Divider_IsAsWideAsItsColumn()
    {
        await OpenAsync("divider-test-page");

        var dividers = Content.Locator("hr.govuk-section-break--visible");
        var fullWidth = await BoxAsync(dividers.Nth(0));
        var half = await BoxAsync(dividers.Nth(2));
        var oneThird = await BoxAsync(dividers.Nth(5));
        var twoThirds = await BoxAsync(dividers.Nth(6));

        Assert.True(half.Width < fullWidth.Width * 0.55, $"half {half.Width} of {fullWidth.Width}");
        Assert.True(oneThird.Width < half.Width, $"one third {oneThird.Width}, half {half.Width}");
        Assert.True(twoThirds.Width > half.Width, $"two thirds {twoThirds.Width}, half {half.Width}");
    }

    // ---- Card ------------------------------------------------------------------------------

    private ILocator Card(string title) =>
        Content.Locator(".dfe-card").Filter(new() { Has = Page.Locator("h3", new() { HasTextString = title }) });

    [Fact]
    public async Task Card_WithALink_MakesItsTitleTheLink()
    {
        await OpenAsync("card-test-page");

        var card = Card("A card with a link and a description");

        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "A card with a link and a description" }))
            .ToHaveAttributeAsync("href", FixtureContent.ShortPagePath);
        await Expect(card.Locator("p")).ToHaveTextAsync("The title is the link. The description says where it goes.");
    }

    [Fact]
    public async Task Card_WithNoLink_HasAPlainTitle()
    {
        await OpenAsync("card-test-page");

        var card = Card("A card with no link");

        await Expect(card.Locator("h3")).ToHaveTextAsync("A card with no link");
        await Expect(card.Locator("a")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Card_WithNoDescription_HasNoEmptyParagraph()
    {
        await OpenAsync("card-test-page");

        await Expect(Card("A card with a title only").Locator("p")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("two", new[] { "First of two", "Second of two" })]
    [InlineData("three", new[] { "First of three", "Second of three", "Third of three" })]
    [InlineData("four", new[] { "First of four", "Second of four", "Third of four", "Fourth of four" })]
    public async Task Card_ARowOfCards_SitsSideBySide_AllTheSameHeight(string row, string[] titles)
    {
        await OpenAsync("card-test-page");

        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var title in titles) boxes.Add(await BoxAsync(Card(title)));

        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(Math.Abs(boxes[i].Y - boxes[0].Y) < 2, $"row of {row}: card {i + 1} is not level with the first");
            Assert.True(boxes[i].X > boxes[i - 1].X + boxes[i - 1].Width - 1, $"row of {row}: card {i + 1} overlaps the one before");
            Assert.True(Math.Abs(boxes[i].Height - boxes[0].Height) < 2, $"row of {row}: card {i + 1} is not the height of the first");
        }
    }

    // A column is not a row: what an author puts in it goes one under the other.
    [Fact]
    public async Task Card_TwoCardsInOneColumn_AreStacked_WithAGapBetweenThem()
    {
        await OpenAsync("card-test-page");

        var top = await BoxAsync(Card("Top left"));
        var bottom = await BoxAsync(Card("Bottom left"));
        var beside = await BoxAsync(Card("Top right"));

        Assert.True(Math.Abs(bottom.X - top.X) < 2, "the second card is not under the first");
        Assert.True(bottom.Y >= top.Y + top.Height + 20, $"top card ends at {top.Y + top.Height}, the one under it starts at {bottom.Y}");
        Assert.True(Math.Abs(beside.Y - top.Y) < 2, "the cards at the top of the two columns are not level");
    }

    [Fact]
    public async Task Card_UnderAHeadingAndText_SitsBelowThem_AtTheLeftOfTheColumn()
    {
        await OpenAsync("card-test-page");

        var heading = await BoxAsync(Content.Locator("#one-card"));
        var card = await BoxAsync(Card("A card with a link and a description"));
        var next = await BoxAsync(Card("A card with no link"));

        Assert.True(card.Y >= heading.Y + heading.Height, "the card is beside its heading, not below it");
        Assert.True(Math.Abs(card.X - heading.X) < 2, $"the card starts at {card.X}, its heading at {heading.X}");
        Assert.True(next.Y >= card.Y + card.Height, "the second card is beside the first, not below it");
    }

    // ---- Summary list ----------------------------------------------------------------------

    [Fact]
    public async Task SummaryList_ShowsEachRowAsANameAndAValue()
    {
        await OpenAsync("summary-list-test-page");

        var first = Content.Locator("dl.govuk-summary-list").First;

        await Expect(first.Locator(".govuk-summary-list__key")).ToHaveTextAsync(["School", "URN", "Phase", "Local authority"]);
        await Expect(first.Locator(".govuk-summary-list__value"))
            .ToHaveTextAsync(["Example Academy", "123456", "Secondary", "Exampleshire"]);
    }

    [Fact]
    public async Task SummaryList_CanHoldOneRow_OrLongText()
    {
        await OpenAsync("summary-list-test-page");

        var lists = Content.Locator("dl.govuk-summary-list");

        await Expect(lists).ToHaveCountAsync(6);
        await Expect(lists.Nth(1).Locator(".govuk-summary-list__row")).ToHaveCountAsync(1);

        // A long name wraps inside its own column and does not push the value off the page.
        var key = await BoxAsync(lists.Nth(2).Locator(".govuk-summary-list__key").First);
        var value = await BoxAsync(lists.Nth(2).Locator(".govuk-summary-list__value").First);
        var list = await BoxAsync(lists.Nth(2));
        Assert.True(value.X >= key.X + key.Width - 1, "the value overlaps its name");
        Assert.True(value.X + value.Width <= list.X + list.Width + 1, "the value runs past the edge of the list");
    }

    [Fact]
    public async Task SummaryList_TwoInARow_SitSideBySide()
    {
        await OpenAsync("summary-list-test-page");

        var lists = Content.Locator("dl.govuk-summary-list");
        var left = await BoxAsync(lists.Nth(3));
        var right = await BoxAsync(lists.Nth(4));

        Assert.True(Math.Abs(left.Y - right.Y) < 2, "the two lists are not level");
        Assert.True(right.X >= left.X + left.Width, "the two lists overlap");
    }

    // ---- Published callout -----------------------------------------------------------------

    [Fact]
    public async Task PublishedCallout_IsABannerTitledPublished_WithTheAuthorsText()
    {
        await OpenAsync("published-callout-test-page");

        var banners = Content.Locator(".govuk-notification-banner");

        await Expect(banners).ToHaveCountAsync(5);
        foreach (var banner in await banners.AllAsync())
        {
            await Expect(banner.Locator(".govuk-notification-banner__title")).ToHaveTextAsync("Published");
            await Expect(banner).ToHaveAttributeAsync("role", "region");
        }
        await Expect(banners.First.Locator(".govuk-notification-banner__heading"))
            .ToHaveTextAsync("The 2026 key stage 4 performance data was published on 15 October 2026.");
    }

    [Fact]
    public async Task PublishedCallout_InANarrowColumn_StaysInsideIt()
    {
        await OpenAsync("published-callout-test-page");

        var banners = Content.Locator(".govuk-notification-banner");
        var wide = await BoxAsync(banners.Nth(0));
        var narrow = await BoxAsync(banners.Nth(2));
        var text = await BoxAsync(banners.Nth(2).Locator(".govuk-notification-banner__heading"));

        Assert.True(narrow.Width < wide.Width * 0.45, $"narrow {narrow.Width}, wide {wide.Width}");
        Assert.True(text.X + text.Width <= narrow.X + narrow.Width + 1, "the text runs past the edge of the banner");
    }

    // ---- Search ----------------------------------------------------------------------------

    private async Task OpenSearchPageAsync()
    {
        await OpenAsync("search-test-page");
        await Expect(SearchForms).ToHaveCountAsync(9);
    }

    [Fact]
    public async Task Search_AsFirstAdded_SearchesTheWholeSite_OnTheSearchPage()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(0);

        await Expect(form).ToHaveAttributeAsync("action", "/search");
        await Expect(form.Locator("label")).ToHaveTextAsync("Search");
        await Expect(form.Locator("input[type=hidden]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Search_OfChosenPages_FindsThePageWithTheWord()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(1);

        await Expect(form.Locator("label")).ToHaveTextAsync("Search the test pages");
        await form.Locator("input[name=q]").FillAsync(FixtureContent.SearchTerm);
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/search\?q=testfixture&pages="));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Search fixture page" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Search_UsesTheAuthorsLabelHintAndButtonText()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(2);

        await Expect(form.Locator("label")).ToHaveTextAsync("Find guidance");
        await Expect(form.Locator("input[name=q]")).ToHaveAttributeAsync("placeholder", "For example, checking exercise");
        await Expect(form.Locator("button[type=submit]")).ToHaveTextAsync("Find");
    }

    [Fact]
    public async Task Search_CanSendItsResultsToAPageOfTheAuthorsOwn()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(3);

        await form.Locator("input[name=q]").FillAsync(FixtureContent.SearchTerm);
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/development-testing/widgets/search-results-test-page\?q=testfixture&pages="));
        await Expect(Content.Locator(".cypmd-search-results").First
            .GetByRole(AriaRole.Link, new() { Name = "Search fixture page" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Search_Instant_SuggestsMatchesAsYouType_AndSaysWhenThereAreNone()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(4);
        var input = form.Locator("input.autocomplete__input");

        await input.FillAsync(FixtureContent.SearchTerm);
        await Expect(form.Locator("li.autocomplete__option").First).ToContainTextAsync("Search fixture page");

        await input.FillAsync("zzqqxxnomatch");
        await Expect(form.Locator("li.autocomplete__option--no-results")).ToHaveTextAsync("Nothing in the test pages matches");
    }

    [Fact]
    public async Task Search_InstantWithNoButton_StillSearchesOnEnter()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(5);

        await Expect(form.Locator("button")).ToHaveCountAsync(0);
        var input = form.Locator("input.autocomplete__input");
        await input.FillAsync("zzqqxxnomatch");
        await input.PressAsync("Enter");

        await Expect(Page).ToHaveURLAsync(new Regex(@"/search\?q=zzqqxxnomatch"));
    }

    [Fact]
    public async Task Search_OfThisPage_ListsItsSections_AndGoesToTheOneChosen()
    {
        await OpenSearchPageAsync();
        var form = SearchForms.Nth(6);

        await form.Locator("input.autocomplete__input").FillAsync("button");
        var option = form.Locator("li.autocomplete__option", new() { HasTextString = "Instant search with no button" });
        await Expect(option).ToBeVisibleAsync();
        await option.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("#instant-search-with-no-button$"));
        await Expect(Content.Locator("#instant-search-with-no-button")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task Search_InANarrowColumn_CanPutItsButtonBelowTheBox()
    {
        await OpenSearchPageAsync();

        var below = SearchForms.Nth(7);
        var belowInput = await BoxAsync(below.Locator("input[name=q]"));
        var belowButton = await BoxAsync(below.Locator("button[type=submit]"));
        var beside = SearchForms.Nth(8);
        var besideInput = await BoxAsync(beside.Locator("input[name=q]"));
        var besideButton = await BoxAsync(beside.Locator("button[type=submit]"));

        Assert.True(belowButton.Y >= belowInput.Y + belowInput.Height, "the button is not below the box");
        Assert.True(besideButton.X >= besideInput.X + besideInput.Width, "the button is not beside the box");
        // The point of the option: in the same width of column, the box is wider.
        Assert.True(belowInput.Width > besideInput.Width * 1.5,
            $"with the button below, the box is {belowInput.Width} wide; with it beside, {besideInput.Width}");
    }

    // A label names its box by id, and so do the menus instant search adds. Nine boxes on one
    // page is where a shared id would show.
    [Fact]
    public async Task Search_ManyOnOnePage_EachHaveTheirOwnIds()
    {
        await OpenSearchPageAsync();
        await Expect(SearchForms.Nth(4).Locator("input.autocomplete__input")).ToBeVisibleAsync();

        var duplicates = await Page.EvaluateAsync<string[]>(@"() => {
            const seen = new Set(), twice = new Set();
            document.querySelectorAll('[id]').forEach(e => (seen.has(e.id) ? twice : seen).add(e.id));
            return [...twice];
        }");

        Assert.Empty(duplicates);
        foreach (var form in await SearchForms.AllAsync())
        {
            var labelFor = await form.Locator("label").GetAttributeAsync("for");
            await Expect(form.Locator($"input[id='{labelFor}']")).ToHaveCountAsync(1);
        }
    }

    // ---- Search results --------------------------------------------------------------------

    private ILocator Results => Content.Locator(".cypmd-search-results");

    [Fact]
    public async Task SearchResults_BeforeASearch_AskForOne()
    {
        await OpenAsync("search-results-test-page");

        await Expect(Results).ToHaveCountAsync(3);
        foreach (var results in await Results.AllAsync())
            await Expect(results).ToHaveTextAsync("Enter a search term to begin.");
    }

    [Fact]
    public async Task SearchResults_ListThePagesThatMatch()
    {
        await OpenAsync("search-results-test-page", $"?q={FixtureContent.SearchTerm}");

        foreach (var results in await Results.AllAsync())
        {
            await Expect(results).ToContainTextAsync("1 result for testfixture.");
            await Expect(results.GetByRole(AriaRole.Link, new() { Name = "Search fixture page" }))
                .ToHaveAttributeAsync("href", "/development-testing/search-fixture");
        }
    }

    [Fact]
    public async Task SearchResults_WithNoMatches_UseTheAuthorsWording()
    {
        await OpenAsync("search-results-test-page", "?q=zzqqxxnomatch");

        await Expect(Results).ToHaveTextAsync(
            ["Nothing in the test pages matches your search.", "No results found.", "No matches."]);
    }

    [Fact]
    public async Task SearchResults_AskForMoreThanOneCharacter()
    {
        await OpenAsync("search-results-test-page", "?q=a");

        foreach (var results in await Results.AllAsync())
            await Expect(results).ToHaveTextAsync("Enter at least 2 characters.");
    }

    [Fact]
    public async Task SearchResults_TheBoxOnThePage_ShowsItsResultsOnThePage()
    {
        await OpenAsync("search-results-test-page");

        await SearchForms.First.Locator("input[name=q]").FillAsync(FixtureContent.SearchTerm);
        await SearchForms.First.Locator("button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/development-testing/widgets/search-results-test-page\?q=testfixture&pages="));
        await Expect(Results.First.GetByRole(AriaRole.Link, new() { Name = "Search fixture page" })).ToBeVisibleAsync();
    }

    // ---- Page navigation -------------------------------------------------------------------

    private ILocator Navigations => Content.Locator(".moj-side-navigation");

    private static async Task<IReadOnlyList<string>> LinksAsync(ILocator navigation) =>
        await navigation.Locator("a").EvaluateAllAsync<string[]>("links => links.map(a => a.getAttribute('href'))");

    [Fact]
    public async Task PageNavigation_AsFirstAdded_ListsTheLevel2And3HeadingsOfThePage()
    {
        await OpenAsync("page-navigation-test-page");

        var links = await LinksAsync(Navigations.Nth(0));

        Assert.Contains("#sections-of-this-page", links);
        Assert.Contains("#a-level-3-heading", links);
        Assert.DoesNotContain("#a-level-4-heading", links);
    }

    [Fact]
    public async Task PageNavigation_ListsOnlyTheLevelsTheAuthorTicked()
    {
        await OpenAsync("page-navigation-test-page");

        var level2Only = await LinksAsync(Navigations.Nth(1));
        var levels2To4 = await LinksAsync(Navigations.Nth(2));

        Assert.Contains("#level-2-only", level2Only);
        Assert.DoesNotContain("#a-level-3-heading", level2Only);
        Assert.Contains("#a-level-3-heading", levels2To4);
        Assert.Contains("#a-level-4-heading", levels2To4);
    }

    [Fact]
    public async Task PageNavigation_ALinkToASection_GoesToIt()
    {
        await OpenAsync("page-navigation-test-page");

        await Navigations.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Pages in a section", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("#pages-in-a-section$"));
        await Expect(Content.Locator("#pages-in-a-section")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task PageNavigation_SetToChildPages_ListsThePagesInTheFolderItNames()
    {
        await OpenAsync("page-navigation-test-page");

        var links = await LinksAsync(Navigations.Nth(3));

        Assert.Equal(9, links.Count);
        Assert.Contains($"{FixtureContent.WidgetsPath}/heading-test-page", links);
        Assert.Contains($"{FixtureContent.WidgetsPath}/page-navigation-test-page", links);
        Assert.All(links, link => Assert.StartsWith($"{FixtureContent.WidgetsPath}/", link));
    }

    [Fact]
    public async Task PageNavigation_WithItsOwnSearchBox_SearchesThePagesItNames()
    {
        await OpenAsync("page-navigation-test-page");
        var form = SearchForms.First;

        await Expect(form.Locator("label")).ToHaveTextAsync("Search test pages");
        await Expect(form.Locator("input[name=scope]")).ToHaveValueAsync("development-testing");
        await form.Locator("input[name=q]").FillAsync(FixtureContent.SearchTerm);
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/search\?q=testfixture&scope=development-testing"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Search fixture page" })).ToBeVisibleAsync();
    }

    // Six widgets, five lists: the last is set to the pages under this page, and there are none.
    [Fact]
    public async Task PageNavigation_WithNothingToList_ShowsNothing()
    {
        await OpenAsync("page-navigation-test-page");

        await Expect(Navigations).ToHaveCountAsync(5);
    }
}
