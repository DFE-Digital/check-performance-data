using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W2")]
public sealed class SoftDeleteWikiPageTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // --- CreatedPage_AppearsInSearch ---

    [Fact]
    public async Task CreatedPage_AppearsInSearch()
    {
        var queryToken = $"e2eseed{Guid.NewGuid().ToString("N")[..8]}";
        await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient,
            title: $"{queryToken} target",
            body: $"Content for {queryToken}.",
            parentId: null,
            TrackedIds);

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/search?q={queryToken}");

        // The freshly-seeded page must surface in /help/search?q={token}. Search.cshtml renders
        // each result as <li> containing <a class="govuk-link" href="/help/{slug}">…</a>; the
        // slug carries the query token.
        var matchingLinks = Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]");
        await Expect(matchingLinks.First).ToBeVisibleAsync();
    }

    // --- SoftDeletedPage_DoesNotAppearInSearch ---

    [Fact]
    public async Task SoftDeletedPage_DoesNotAppearInSearch()
    {
        var queryToken = $"e2edel{Guid.NewGuid().ToString("N")[..8]}";
        var id = await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient,
            title: $"{queryToken} soon-deleted",
            body: $"Content for {queryToken}, will be soft-deleted before search.",
            parentId: null,
            TrackedIds);

        await SeedHelpers.SoftDeleteWikiPageAsync(Fixture.SeedClient, id);

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/search?q={queryToken}");

        // The soft-delete EF query filter (HasQueryFilter(w => !w.IsDeleted)) MUST hide this
        // page from the search results. Zero matching anchors is the contract.
        await Expect(Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]")).ToHaveCountAsync(0);
    }

    // --- SoftDeletedPage_AppearsInDeletedPagesList ---

    [Fact]
    public async Task SoftDeletedPage_AppearsInDeletedPagesList()
    {
        var queryToken = $"e2edl{Guid.NewGuid().ToString("N")[..8]}";
        var id = await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient,
            title: $"{queryToken} restorable",
            body: $"Content for {queryToken}.",
            parentId: null,
            TrackedIds);

        await SeedHelpers.SoftDeleteWikiPageAsync(Fixture.SeedClient, id);

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/deleted");

        // The deleted-pages admin view (Views/Help/Deleted.cshtml) renders one <tr> per
        // soft-deleted page with the title in a <td class="govuk-table__cell"> cell.
        await Expect(Page.GetByText(queryToken).First).ToBeVisibleAsync();
    }
}
