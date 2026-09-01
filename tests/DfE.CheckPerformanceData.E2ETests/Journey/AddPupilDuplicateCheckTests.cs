using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#297780 "Add Pupil duplicate check": after the Add journey's learner-details page is
// submitted, the backend looks the entered first name + surname + DOB up against the full
// school-window roll (included + non-included). No match → the Add journey continues unchanged.
// Matches → a GDS warning page offers Abort / Continue-adding, plus — for a single non-included
// match — "Include this pupil", and for an already-included match a warning with no include option.
//
// These browser tests pin the seam at the only place it surfaces end-to-end: the redirect after
// the learner-details post. They use the seeded Kingsmead School pupil blobs:
//   * included "Alice Smith"  born 01/01/2010 (index 0), UPN A860407000001B
//   * non-included "Bob Johnson" born 02/02/2010 (index 201), UPN A860407000202B
//   * a deliberate same-name/DOB pair "Casey Carter" born 15/03/2010 — one included, one not
//     (SeedPupilData.GenerateDuplicateMatchPair) — which drives the Multiple scenario below
[Collection("E2E")]
public sealed class AddPupilDuplicateCheckTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId), which Kingsmead's
    // pupil blobs are uploaded against.
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // ── US1: no match continues; a match surfaces the warning page ─────────

    [RetryFact(3)]
    public async Task NoMatch_ContinuesToAdmissionDetails()
    {
        // "Alice Taylor" (born 11/01/2010) is not a seeded name/DOB pair, so the Add journey must
        // proceed to the next page exactly as if the duplicate check had never run.
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Taylor", day: "11", month: "1", year: "2010",
            sex: "F", upn: "A123456789012");

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/admission-details");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("details at your school");
    }

    [RetryFact(3)]
    public async Task ExistingMatch_ShowsTheDuplicateWarningPage()
    {
        // Alice Smith (included, born 01/01/2010) already exists on the roll, so the learner-details
        // post must land on the duplicate-check page, not continue to admission-details.
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Smith", day: "1", month: "1", year: "2010",
            sex: "F", upn: "");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil may already be on the roll");
        Assert.Contains("/duplicate-check", Page.Url);
    }

    // ── US2: single non-included match offers Include + Continue ───────────

    [RetryFact(3)]
    public async Task SingleNonIncludedMatch_OfferInclude_StartsTheIncludeJourney()
    {
        // Bob Johnson (non-included, born 02/02/2010) — the reported bug: the Include option was
        // missing for a single non-included match. It must now offer "Include this pupil".
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Bob", lastName: "Johnson", day: "2", month: "2", year: "2010",
            sex: "M", upn: "");

        var includeButton = Page.GetByRole(AriaRole.Button, new() { Name = "Include this pupil" });
        await Expect(includeButton).ToBeVisibleAsync();

        await includeButton.ClickAsync();

        // The Include journey's post-search page is "evidence" (Include_KS4June.json select-pupil
        // → evidence). Seeding the Include hand-off must route there.
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/evidence");
    }

    [RetryFact(3)]
    public async Task SingleNonIncludedMatch_ContinueAdding_ProceedsWithAdd()
    {
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Bob", lastName: "Johnson", day: "2", month: "2", year: "2010",
            sex: "M", upn: "");

        var continueButton = Page.GetByRole(AriaRole.Button, new() { Name = "Continue adding" });
        await Expect(continueButton).ToBeVisibleAsync();
        await continueButton.ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/admission-details");
    }

    // ── US3: an already-included match warns but never offers Include ───────

    [RetryFact(3)]
    public async Task AlreadyIncludedMatch_WarnsAndDoesNotOfferInclude()
    {
        // Alice Smith is already included. The warning must show with Abort + Continue, and
        // crucially there must be NO "Include this pupil" option (T033).
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Smith", day: "1", month: "1", year: "2010",
            sex: "F", upn: "");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil may already be on the roll");
        Assert.Equal(0, await Page.GetByRole(AriaRole.Button, new() { Name = "Include this pupil" }).CountAsync());

        // Abort returns to check-your-data (aborting the Add journey).
        await Page.GetByRole(AriaRole.Button, new() { Name = "Abort adding this pupil" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Check your pupil data");
    }

    // ── US3: multiple matches list both, with per-non-included Switch to Include ─

    [RetryFact(3)]
    public async Task MultipleMatches_ShowTheListWithInclusionStatusAndSwitchToInclude()
    {
        // "Casey Carter" (born 15/03/2010) is seeded twice — once included, once not — so the
        // duplicate check must land on the Multiple branch: a table listing both rows with their
        // inclusion status, a Switch-to-Include button only on the non-included row, and the
        // Abort + Continue actions (T032).
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Casey", lastName: "Carter", day: "15", month: "3", year: "2010",
            sex: "F", upn: "");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil may already be on the roll");
        var warning = Page.Locator(".govuk-warning-text");
        await Expect(warning).ToContainTextAsync("may already be pupils");

        var rows = Page.Locator("table.govuk-table tbody tr");
        Assert.Equal(2, await rows.CountAsync());

        // Both rows carry the same displayed name + DOB, and the status column reports each
        // inclusion state so the user can tell them apart.
        var tableText = await Page.Locator("table.govuk-table").InnerTextAsync();
        Assert.Contains("Carter, Casey", tableText);
        Assert.Contains("15/03/2010", tableText);
        Assert.Contains("Included", tableText);
        Assert.Contains("Not included", tableText);

        // Only the non-included row offers an action — exactly one Switch-to-Include button.
        var switchButtons = Page.GetByRole(AriaRole.Button, new() { Name = "Switch to include" });
        Assert.Equal(1, await switchButtons.CountAsync());

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue adding" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Abort adding this pupil" })).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task MultipleMatches_SwitchToInclude_StartsTheIncludeJourney()
    {
        // From the same Multiple list, clicking Switch-to-Include on the non-included row seeds
        // that pupil into the Include journey and routes past its select-pupil search page — the
        // evidence page, exactly as Include-this-pupil does for a single non-included match (T034).
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Casey", lastName: "Carter", day: "15", month: "3", year: "2010",
            sex: "F", upn: "");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to include" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/evidence");
    }

    // ── helpers (mirror AddPupilJourneyTests) ───────────────────────────────

    private async Task StartAddJourneyAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks4JuneWindowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Add']").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/learner-details");
    }

    private async Task FillLearnerDetailsAsync(
        string firstName, string lastName, string day, string month, string year, string sex, string upn)
    {
        await Page.Locator("#q_first_name").FillAsync(firstName);
        await Page.Locator("#q_last_name").FillAsync(lastName);
        await FillDateAsync("date-of-birth", day, month, year);
        await Page.Locator($"input[name='q_sex'][value='{sex}']").CheckAsync(new() { Force = true });
        if (upn.Length > 0)
            await Page.Locator("#q_upn").FillAsync(upn);
        await ContinueAsync();
    }

    private async Task FillDateAsync(string questionId, string day, string month, string year)
    {
        var baseId = $"q_{questionId.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day);
        await Page.Locator($"#{baseId}_month").FillAsync(month);
        await Page.Locator($"#{baseId}_year").FillAsync(year);
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
}
