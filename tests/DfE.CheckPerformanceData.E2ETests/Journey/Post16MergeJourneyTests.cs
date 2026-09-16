using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#298701: the 16-19 "Merge duplicate student records" journey, driven through a real browser.
//
// These cover what only a browser can: that the journey holds together end-to-end across every
// redirect, that the pupil-search autocomplete works with Post16 labels (CYPMD ID, ULN, DOB,
// inclusion status), that only included students are returned for the first record search, and
// that the reference number uses the "16to19" format.
//
// The seeded Post16 window (DevDataSeeder.Post16CheckingWindowId) carries Kingsmead School's
// Post16 pupils: Alice Smith (CYPMD ID 500001, PINCL 501, included) and Bob Smith (CYPMD ID
// 500002, PINCL 502, included).
[Collection("E2E")]
public sealed class Post16MergeJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded Post16 window (DevDataSeeder.Post16CheckingWindowId).
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    // Kingsmead's first seeded Post16 included student — PINCL 501.
    private const string FirstStudentCypmdId = "500001";
    private const string FirstStudentName = "Alice Smith";

    // Kingsmead's second seeded Post16 included student — PINCL 502.
    private const string SecondStudentCypmdId = "500002";
    private const string SecondStudentName = "Bob Smith";

    // ── The full merge journey, end to end ──────────────────────────────────

    [RetryFact(3)]
    public async Task HappyPath_SubmitsAndShowsAReference()
    {
        await StartMergeJourneyAsync();

        // First record: select included student Alice Smith (CYPMD ID 500001).
        await ChooseStudentAsync(FirstStudentCypmdId);

        // Second record: select Bob Smith (CYPMD ID 500002).
        await ChooseMatchStudentAsync(SecondStudentCypmdId);

        // Evidence page — both file and text are optional, skip straight through.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");
        await ContinueAsync();

        // Check answers.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Merge", summary);
        Assert.Contains(FirstStudentName, summary);
        Assert.Contains(SecondStudentName, summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var match = System.Text.RegularExpressions.Regex.Match(panel, @"CYPMD_16to19_[0-9A-F]{7}");
        Assert.True(match.Success, $"No reference number in the confirmation panel: {panel}");
    }

    // ── First record search: only included students appear ──────────────────

    [RetryFact(3)]
    public async Task FirstRecordSearch_ShowsIncludedStudentsOnly()
    {
        await StartMergeJourneyAsync();

        // Type a partial CYPMD ID that matches the included Alice Smith.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-pupil");
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(FirstStudentCypmdId);

        // The autocomplete should show the included student with the INCLUDED tag.
        var option = Page.Locator("li[role='option']")
            .GetByText("INCLUDED");
        await Expect(option.First).ToBeVisibleAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task StartMergeJourneyAsync()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.GetByLabel("Merge").First.CheckAsync(new() { Force = true });
        await ContinueAsync();
        await AdvanceToAsync("pupil-search/select-pupil", "choosing Merge");
    }

    private async Task ChooseStudentAsync(string cypmdId)
    {
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(cypmdId);
        var option = Page.Locator("li[role='option']").GetByText(cypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task ChooseMatchStudentAsync(string cypmdId)
    {
        await AdvanceToAsync("pupil-search/select-match-pupil", "choosing the first student");
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(cypmdId);
        var option = Page.Locator("li[role='option']").GetByText(cypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    // Wait for the journey to move on, and say why it didn't when it doesn't.
    //
    // A step that the server rejects re-renders the page it was already on, so the plain
    // WaitForURLAsync this replaces simply ran out of time and reported "Timeout 30000ms
    // exceeded". The reason was on the screen the whole time, in the GDS error summary: a
    // student who already has a submitted request is refused here, which is what happened
    // every time the environment carried a request left over from an earlier run. Reading
    // the summary turns a half-day of investigation into the first line of the failure.
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
