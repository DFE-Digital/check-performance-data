using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#304118 / #510 / #716: the KS4 "Merge duplicate pupil records" journey, driven through a real
// browser. Both search pages match the first name, last name, CYPMD ID, UPN or date of birth, as
// their hint says (#716). Each KS4 suggestion shows its CYPMD ID, so two records with the same name
// and date of birth can be told apart. The second-record page never offers the first record.
//
// Only a browser can show this. The ID reaches the dropdown through the suggestions endpoint's JSON
// `label`, and the first record is excluded through the parameter the page sends. These drive the
// real search box, the real autocomplete and the real confirmation.
//
// #747 (AB#312017): the KS4 Autumn window has the same merge journey (Merge_KS4Autumn.json is a copy
// of the KS4 June file), so every test runs on both KS4 windows. Both seeded windows carry a pair
// built for this journey (SeedPupilData.GenerateDuplicateMatchPair, SeedPupilData.Ks4PairWindowIds):
// Casey Carter, born 15/03/2010, twice — CYPMD ID 800001 and 800002. Identical name AND date of
// birth, which is exactly why an ID has to be on the row: without it the two options are
// indistinguishable.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class Ks4MergeJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4 June window (DevDataSeeder.KeyStage4JuneCheckingWindowId) and seeded KS4 Autumn
    // window (DevDataSeeder.KeyStage4AutumnCheckingWindowId) — the windows SeedPupilData writes
    // Kingsmead's duplicate match pair into. Strings, so the theory rows list separately.
    private const string Ks4JuneWindowId = "F34D285B-8660-4D12-9C30-787328DEAA0A";
    private const string Ks4AutumnWindowId = "7D2E9A14-5C38-4B6F-A0E1-3F8B6C2D4A97";

    private const string SharedName = "Casey";
    private const string SharedSurname = "Carter";

    private const string FirstRecordCypmdId = "800001";   // Pincl-included
    private const string SecondRecordCypmdId = "800002";  // not included

    // ── The full merge journey, end to end ──────────────────────────────────

    [RetryTheory]
    [InlineData(Ks4JuneWindowId, "CYPMD_KS4June_")]
    [InlineData(Ks4AutumnWindowId, "CYPMD_KS4Autumn_")]
    [Trait("Category", "Smoke")]
    public async Task HappyPath_FirstRecordByName_SecondRecordByCyPmdId_ShowsBothOnTheConfirmation(
        string window, string referencePrefix)
    {
        var windowId = Guid.Parse(window);
        await StartMergeJourneyAsync(windowId);

        // The clerk finds the first record by name.
        await ChooseFirstRecordByNameAsync(windowId);

        // The clerk finds the second record by its CYPMD ID.
        await ChooseSecondRecordByCyPmdIdAsync(windowId, SecondRecordCypmdId);

        // Evidence page — both file and text are optional here, so it is skipped straight through.
        await Page.WaitForURLAsync($"**/Journey/{windowId}/page/evidence");
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

        await Page.WaitForURLAsync($"**/Journey/{windowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var reference = System.Text.RegularExpressions.Regex.Match(panel, referencePrefix + "[0-9A-F]{7}");
        Assert.True(reference.Success, $"No {referencePrefix} reference number in the confirmation panel: {panel}");
    }

    // ── The second-record page searches every field in its hint ─────────────

    [RetryTheory]
    [InlineData(Ks4JuneWindowId)]
    [InlineData(Ks4AutumnWindowId)]
    public async Task SecondRecordPage_TypingTheSharedSurname_OffersOnlyTheOtherRecord(string window)
    {
        // #716: the hint names the first name and last name, so a name finds the second record.
        // Both records share the name, so the row must carry the ID, and the first record is
        // never on offer as its own match.
        var windowId = Guid.Parse(window);
        await StartMergeJourneyAsync(windowId);
        await ChooseFirstRecordByNameAsync(windowId);
        await AdvanceToAsync(windowId, "pupil-search/select-match-pupil", "choosing the first record");

        await FillSearchAsync(SharedSurname);

        await Expect(OptionShowingCypmdId(SecondRecordCypmdId).First).ToBeVisibleAsync();
        await Expect(OptionShowingCypmdId(FirstRecordCypmdId)).ToHaveCountAsync(0);
    }

    [RetryTheory]
    [InlineData(Ks4JuneWindowId)]
    [InlineData(Ks4AutumnWindowId)]
    public async Task SecondRecordPage_TypingACyPmdIdPrefix_OffersSuggestionsThatShowTheirId(string window)
    {
        var windowId = Guid.Parse(window);
        await StartMergeJourneyAsync(windowId);
        await ChooseFirstRecordByNameAsync(windowId);
        await AdvanceToAsync(windowId, "pupil-search/select-match-pupil", "choosing the first record");

        // A prefix, not the whole ID — "Start typing" means partial typing.
        await FillSearchAsync("8000");

        // US2 through the endpoint's JSON label: the row has to carry the ID the clerk typed,
        // because both records share a name and a date of birth.
        var option = OptionShowingCypmdId(SecondRecordCypmdId);
        await Expect(option.First).ToBeVisibleAsync();

        // And the already-chosen first record is never on offer as its own match.
        var firstRecord = OptionShowingCypmdId(FirstRecordCypmdId);
        await Expect(firstRecord).ToHaveCountAsync(0);
    }

    [RetryTheory]
    [InlineData(Ks4JuneWindowId)]
    [InlineData(Ks4AutumnWindowId)]
    public async Task FirstRecordPage_SearchesByName(string window)
    {
        var windowId = Guid.Parse(window);
        await StartMergeJourneyAsync(windowId);
        await AdvanceToAsync(windowId, "pupil-search/select-pupil", "choosing Merge");

        await FillSearchAsync(SharedSurname);

        var option = Page.Locator("li[role='option']").GetByText($"{SharedSurname}, {SharedName}");
        await Expect(option.First).ToBeVisibleAsync();
    }

    // ── #747: the KS4 Autumn selection rules through the real pages ──────────

    // Non-included Kingsmead pupils have the surnames Johnson to Thompson (SeedPupilData: the
    // non-included index block starts at 200 and the surname is index (n / 20) % 20); no included
    // pupil has one of those surnames. Alice Johnson, index 200, is CYPMD 000201.
    private const string NonIncludedOnlySurname = "Johnson";
    private const string NonIncludedPupilCypmdId = "000201";

    [RetryFact]
    public async Task Ks4Autumn_FirstRecordPage_OffersNoNonIncludedPupil_ButTheSecondRecordPageDoes()
    {
        var windowId = Guid.Parse(Ks4AutumnWindowId);
        await StartMergeJourneyAsync(windowId);
        await AdvanceToAsync(windowId, "pupil-search/select-pupil", "choosing Merge");

        // AC: a pupil in the "not included" list is not returned for the first record.
        await FillSearchAsync(NonIncludedOnlySurname);
        await ExpectNoSuggestionsAsync();

        // AC: the included copy of the pair is, and only the included copy.
        await FillSearchAsync(SharedSurname);
        await Expect(OptionShowingCypmdId(FirstRecordCypmdId).First).ToBeVisibleAsync();
        await Expect(OptionShowingCypmdId(SecondRecordCypmdId)).ToHaveCountAsync(0);
        await OptionShowingCypmdId(FirstRecordCypmdId).First.ClickAsync();
        await ContinueAsync();

        // AC: the second record may be a pupil from the "not included" list.
        await AdvanceToAsync(windowId, "pupil-search/select-match-pupil", "choosing the first record");
        await FillSearchAsync(NonIncludedPupilCypmdId);
        var option = OptionShowingCypmdId(NonIncludedPupilCypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await Expect(option.First).ToContainTextAsync("Status: NOT INCLUDED");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task StartMergeJourneyAsync(Guid windowId)
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{windowId}");
        await Page.GetByLabel("Merge").First.CheckAsync();
        await ContinueAsync();
    }

    private async Task ChooseFirstRecordByNameAsync(Guid windowId)
    {
        await AdvanceToAsync(windowId, "pupil-search/select-pupil", "choosing Merge");
        await FillSearchAsync(SharedSurname);
        var option = Page.Locator("li[role='option']").GetByText($"{SharedSurname}, {SharedName}");
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task ChooseSecondRecordByCyPmdIdAsync(Guid windowId, string cypmdId)
    {
        await AdvanceToAsync(windowId, "pupil-search/select-match-pupil", "choosing the first record");
        await FillSearchAsync(cypmdId);
        var option = OptionShowingCypmdId(cypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    // Since #623 (PR #630) the KS4 suggestion reads
    // "Surname, Firstname, (CYPMD ID: 800002, UPN: …, DOB: …, Status: …)" — the ID is no longer a
    // bare "(800002)" at the end. The trailing comma stops "800002" matching a longer ID.
    private ILocator OptionShowingCypmdId(string cypmdId) =>
        Page.Locator("li[role='option']").GetByText($"CYPMD ID: {cypmdId},");

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
    /// It carries no <c>role="option"</c>, hence the two assertions. (PR #762 removed this helper
    /// with the test that used it; #747 needs it again for the first-record rule.)
    /// </summary>
    private async Task ExpectNoSuggestionsAsync()
    {
        await Expect(Page.Locator(".autocomplete__option--no-results"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.Locator("li[role='option']")).ToHaveCountAsync(0);
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    // Wait for the journey to move on, and say why it didn't when it doesn't.
    //
    // A step the server rejects re-renders the page it was already on, so the plain WaitForURLAsync
    // this replaces ran out of time and reported "Timeout 30000ms exceeded" with the reason sitting
    // in the GDS error summary the whole time.
    private async Task AdvanceToAsync(Guid windowId, string expectedPathSuffix, string afterStep)
    {
        try
        {
            await Page.WaitForURLAsync($"**/Journey/{windowId}/{expectedPathSuffix}");
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
