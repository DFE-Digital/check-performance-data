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
// The seeded KS4June window carries a pair built for this journey (SeedPupilData.GenerateDuplicateMatchPair):
// Casey Carter, born 15/03/2010, twice — CYPMD ID 800001 and 800002. Identical name AND date of birth,
// which is exactly why an ID has to be on the row: without it the two options are indistinguishable.
[Trait("Category", "FullRegression")]
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
    [Trait("Category", "Smoke")]
    public async Task HappyPath_FirstRecordByName_SecondRecordByCyPmdId_ShowsBothOnTheConfirmation()
    {
        await StartMergeJourneyAsync();

        // The clerk finds the first record by name.
        await ChooseFirstRecordByNameAsync();

        // The clerk finds the second record by its CYPMD ID.
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

    // ── The second-record page searches every field in its hint ─────────────

    [RetryFact]
    public async Task SecondRecordPage_TypingTheSharedSurname_OffersOnlyTheOtherRecord()
    {
        // #716: the hint names the first name and last name, so a name finds the second record.
        // Both records share the name, so the row must carry the ID, and the first record is
        // never on offer as its own match.
        await StartMergeJourneyAsync();
        await ChooseFirstRecordByNameAsync();
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first record");

        await FillSearchAsync(SharedSurname);

        await Expect(OptionShowingCypmdId(SecondRecordCypmdId).First).ToBeVisibleAsync();
        await Expect(OptionShowingCypmdId(FirstRecordCypmdId)).ToHaveCountAsync(0);
    }

    [RetryFact]
    public async Task SecondRecordPage_TypingACyPmdIdPrefix_OffersSuggestionsThatShowTheirId()
    {
        await StartMergeJourneyAsync();
        await ChooseFirstRecordByNameAsync();
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first record");

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

    [RetryFact]
    public async Task FirstRecordPage_SearchesByName()
    {
        await StartMergeJourneyAsync();
        await AdvanceToAsync("pupil-search/select-pupil", "choosing Merge");

        await FillSearchAsync(SharedSurname);

        var option = Page.Locator("li[role='option']").GetByText($"{SharedSurname}, {SharedName}");
        await Expect(option.First).ToBeVisibleAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task StartMergeJourneyAsync()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.GetByLabel("Merge").First.CheckAsync();
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