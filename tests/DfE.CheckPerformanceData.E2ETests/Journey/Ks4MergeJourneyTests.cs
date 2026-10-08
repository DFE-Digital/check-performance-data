using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#304118 / #510: the KS4 "Merge duplicate pupil records" journey, driven through a real browser.
// The second-record page's own copy — "What is the CYPMD ID of the second duplicate record to be
// merged?" and "Start typing ID to search records" — promised an ID-only search while the underlying
// search matched names, split names, UPNs and dates of birth as well. So the search is now restricted
// to the CYPMD ID, and because KS4 suggestions read "Surname, Firstname, DOB" with no ID in them, each
// offered record shows its own.
//
// Only a browser can show this. The restriction is a query-string parameter the page sends, the
// narrowing happens in the suggestions endpoint the autocomplete calls, and the ID reaches the
// dropdown through the endpoint's JSON `label` — so unit tests over the format class cannot see
// whether the page actually sends the parameter or whether the rendered row carries the ID. These
// drive the real search box, the real autocomplete and the real confirmation.
//
// The seeded KS4June window carries a pair built for this journey (SeedPupilData.GenerateDuplicateMatchPair):
// Casey Carter, born 15/03/2010, twice — CYPMD ID 800001 and 800002. Identical name AND date of birth,
// which is exactly why an ID has to be on the row: without it the two options are indistinguishable.
[Trait("Category", "Smoke")]
[Collection("E2E")]
public sealed class Ks4MergeJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId) — the window
    // SeedPupilData writes Kingsmead's duplicate match pair into.
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private const string SharedName = "Casey";
    private const string SharedSurname = "Carter";

    private const string FirstRecordCypmdId = "800001";   // Pincl-included
    private const string SecondRecordCypmdId = "800002";  // not included

    // ── The full merge journey, end to end ──────────────────────────────────

    [RetryFact]
    public async Task HappyPath_FirstRecordByName_SecondRecordByCyPmdId_ShowsBothOnTheConfirmation()
    {
        await StartMergeJourneyAsync();

        // The FIRST record page is untouched by this feature: it still searches by name, so the
        // clerk finds Casey Carter the way they always have.
        await ChooseFirstRecordByNameAsync();

        // The SECOND record page is the one that changed. Its copy names the CYPMD ID, so the clerk
        // types the ID.
        await ChooseSecondRecordByCyPmdIdAsync(SecondRecordCypmdId);

        // Evidence page — both file and text are optional here, so it is skipped straight through.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Merge", summary);

        var firstRecord = Page.Locator(".govuk-summary-list__row", new() { HasText = "First record to merge" });
        await Expect(firstRecord).ToContainTextAsync($"{SharedName} {SharedSurname}");
        await Expect(firstRecord).ToContainTextAsync($"({FirstRecordCypmdId})");
        var secondRecord = Page.Locator(".govuk-summary-list__row", new() { HasText = "Second record to merge" });
        await Expect(secondRecord).ToContainTextAsync($"{SharedName} {SharedSurname}");
        await Expect(secondRecord).ToContainTextAsync($"({SecondRecordCypmdId})");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var reference = System.Text.RegularExpressions.Regex.Match(panel, @"CYPMD_KS4June_[0-9A-F]{7}");
        Assert.True(reference.Success, $"No reference number in the confirmation panel: {panel}");
    }

    // ── The second-record page searches by ID and offers nothing else ────────

    [RetryFact]
    public async Task SecondRecordPage_TypingTheSharedSurname_OffersNoOptions()
    {
        // The defect, through the UI: this pupil's surname and forename are the pair's shared name,
        // so before the fix typing either offered the records. The page's own label and hint say to
        // type an ID, so it must offer nothing.
        await StartMergeJourneyAsync();
        await ChooseFirstRecordByNameAsync();
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first record");

        await FillSearchAsync(SharedSurname);

        await ExpectNoSuggestionsAsync();
        await FillSearchAsync(SharedName);
        await ExpectNoSuggestionsAsync();
        await FillSearchAsync($"{SharedName} {SharedSurname}");
        await ExpectNoSuggestionsAsync();
    }

    [RetryFact]
    public async Task SecondRecordPage_TypingACyPmdIdPrefix_OffersSuggestionsThatShowTheirId()
    {
        await StartMergeJourneyAsync();
        await ChooseFirstRecordByNameAsync();
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first record");

        // A prefix, not the whole ID — "Start typing ID to search records" means partial typing.
        await FillSearchAsync("8000");

        // US2 through the endpoint's JSON label: the row has to carry the ID the clerk typed,
        // because both records share a name and a date of birth.
        var option = Page.Locator("li[role='option']").GetByText($"({SecondRecordCypmdId})");
        await Expect(option.First).ToBeVisibleAsync();

        // And the already-chosen first record is never on offer as its own match.
        var firstRecord = Page.Locator("li[role='option']").GetByText($"({FirstRecordCypmdId})");
        await Expect(firstRecord).ToHaveCountAsync(0);
    }

    [RetryFact]
    public async Task FirstRecordPage_StillSearchesByNameAndOffersNoId()
    {
        // The guard for the other half of the feature. This page is in the same journey as the
        // narrowed one, so a change made at journey level rather than page level would break it —
        // and it would break Remove, Include and every 16-19 search with it.
        await StartMergeJourneyAsync();
        await AdvanceToAsync("pupil-search/select-pupil", "choosing Merge");

        await FillSearchAsync(SharedSurname);

        var option = Page.Locator("li[role='option']").GetByText($"{SharedSurname}, {SharedName}");
        await Expect(option.First).ToBeVisibleAsync();
        await ExpectNoBracketedIdAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task StartMergeJourneyAsync()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.GetByLabel("Merge").First.CheckAsync(new() { Force = true });
        await ContinueAsync();
    }

    private async Task ChooseFirstRecordByNameAsync()
    {
        await AdvanceToAsync("pupil-search/select-pupil", "choosing Merge");
        await FillSearchAsync(SharedSurname);
        var option = Page.Locator("li[role='option']").GetByText($"{SharedSurname}, {SharedName}");
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task ChooseSecondRecordByCyPmdIdAsync(string cypmdId)
    {
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first record");
        await FillSearchAsync(cypmdId);
        var option = Page.Locator("li[role='option']").GetByText($"({cypmdId})");
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task FillSearchAsync(string query)
    {
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(query);
    }

    /// <summary>
    /// Waits for the autocomplete to settle on having nothing to offer. Asserting
    /// <c>ToHaveCountAsync(0)</c> straight after filling would pass against a dropdown that had not
    /// yet had a chance to appear, so this waits for the empty-state row the component renders once a
    /// response with no options comes back — which is also the "no results" message the clerk reads.
    /// It carries no <c>role="option"</c>, hence the two assertions.
    /// </summary>
    private async Task ExpectNoSuggestionsAsync()
    {
        await Expect(Page.Locator(".autocomplete__option--no-results"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.Locator("li[role='option']")).ToHaveCountAsync(0);
    }

    private async Task ExpectNoBracketedIdAsync()
    {
        var options = Page.Locator("li[role='option']");
        await Expect(options.First).ToBeVisibleAsync();
        for (var i = 0; i < await options.CountAsync(); i++)
            Assert.DoesNotContain("(", await options.Nth(i).InnerTextAsync());
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    // Wait for the journey to move on, and say why it didn't when it doesn't.
    //
    // A step the server rejects re-renders the page it was already on, so the plain WaitForURLAsync
    // this replaces ran out of time and reported "Timeout 30000ms exceeded" with the reason sitting
    // in the GDS error summary the whole time.
    private async Task AdvanceToAsync(string expectedPathSuffix, string afterStep)
    {
        try
        {
            await Page.WaitForURLAsync($"**/Journey/{WindowId}/{expectedPathSuffix}");
        }
        catch (TimeoutException)
        {
            var summary = Page.Locator(".govuk-error-summary");
            var problem = await summary.CountAsync() > 0
                ? Whitespace.Replace(await summary.InnerTextAsync(), " ").Trim()
                : "no error summary was rendered";

            Assert.Fail(
                $"The journey did not reach {expectedPathSuffix} after {afterStep}. " +
                $"It is still on {Page.Url}. The page says: {problem}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Whitespace = new(@"\s+");
}