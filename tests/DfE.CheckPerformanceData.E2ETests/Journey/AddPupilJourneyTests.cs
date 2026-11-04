using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#297310: the "Add a pupil not found on my DfE pupil roll" journey (simple path), driven
// through a real browser. #749 (AB#311996): the KS4 Autumn window has the same journey
// (Add_KS4Autumn.json is a copy of Add_KS4June.json), so the tests run on both KS4 windows.
//
// The Add journey has no pupil-search step — the learner-details page's own answers ARE the
// pupil (AddPupilJourney.BuildPupil). These tests cover what only a browser can: that the whole
// page chain holds together across redirects with no dataset pupil behind it, that the future-date
// rules and required-field errors render correctly, and that a Post16 window (no Add flow exists
// for it) never offers the option.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class AddPupilJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4 June window (DevDataSeeder.KeyStage4JuneCheckingWindowId) and the seeded KS4
    // Autumn window (DevDataSeeder.KeyStage4AutumnCheckingWindowId). Add doesn't need their pupil
    // blobs, only an open KS4 window with an Add flow. Strings, so they can be InlineData.
    private const string Ks4JuneWindowId = "F34D285B-8660-4D12-9C30-787328DEAA0A";
    private const string Ks4AutumnWindowId = "7D2E9A14-5C38-4B6F-A0E1-3F8B6C2D4A97";

    // The seeded "16 to 19 Oct" window — no Add_Post16.json flow exists, so the option must not appear.
    private static readonly Guid Post16WindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");

    [RetryTheory]
    [InlineData(Ks4JuneWindowId, "CYPMD_KS4June_")]
    [InlineData(Ks4AutumnWindowId, "CYPMD_KS4Autumn_")]
    [Trait("Category", "Smoke")]
    public async Task HappyPath_SubmitsAndShowsAReference(string window, string referencePrefix)
    {
        var windowId = Guid.Parse(window);
        await StartAddJourneyAsync(windowId);

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Newpupil",
            day: "1", month: "9", year: "2010",
            sex: "F", upn: "A123456789012");

        await Page.WaitForURLAsync($"**/Journey/{windowId}/page/admission-details");
        await FillDateAsync("admission-date", "1", "9", "2025");
        await Page.Locator("input[name='q_year_group'][value='10']").CheckAsync();
        await Page.Locator("input[name='q_sen_status'][value='N']").CheckAsync();
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{windowId}/page/evidence");
        await Page.Locator("#q_how_evidence_supports").FillAsync("Pupil joined mid-term from another school.");
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{windowId}/summary");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        // No combined "Pupil name" row: that row's Change link goes to a pupil-search page, which
        // this journey has none of. The first and last name rows below carry the same name with
        // Change links that work.
        Assert.DoesNotContain("Pupil name", summary);
        Assert.Contains("Alice", summary);
        Assert.Contains("Newpupil", summary);
        Assert.Contains("First name", summary);
        Assert.Contains("Year group", summary);
        Assert.Contains("SEN status", summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{windowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var match = System.Text.RegularExpressions.Regex.Match(panel, referencePrefix + "[0-9A-F]{7}");
        Assert.True(match.Success, $"No reference number starting {referencePrefix} in the confirmation panel: {panel}");
    }

    [RetryTheory]
    [InlineData(Ks4JuneWindowId)]
    [InlineData(Ks4AutumnWindowId)]
    public async Task MissingRequiredFields_ShowTheErrors(string window)
    {
        await StartAddJourneyAsync(Guid.Parse(window));

        await ContinueAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var text = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains("Enter the pupil's first name", text);
        Assert.Contains("Enter the pupil's last name", text);
        Assert.Contains("Enter the pupil's date of birth", text);
        Assert.Contains("Select the pupil's sex", text);
        Assert.DoesNotContain("Unique pupil number", text);
    }

    [RetryTheory]
    [InlineData(Ks4JuneWindowId)]
    [InlineData(Ks4AutumnWindowId)]
    public async Task FutureDateOfBirth_IsBlocked(string window)
    {
        await StartAddJourneyAsync(Guid.Parse(window));

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Newpupil",
            day: "1", month: "9", year: "2099",
            sex: "F", upn: "");

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var text = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains("Date of birth must be in the past", text);
    }

    [RetryFact]
    public async Task Post16Window_DoesNotOfferAdd()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        var option = Page.Locator("input[name='SelectedWhatToChange'][value='Add']");
        Assert.Equal(0, await option.CountAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task StartAddJourneyAsync(Guid windowId)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{windowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Add']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{windowId}/page/learner-details");
    }

    private async Task FillLearnerDetailsAsync(
        string firstName, string lastName, string day, string month, string year, string sex, string upn)
    {
        await Page.Locator("#q_first_name").FillAsync(firstName);
        await Page.Locator("#q_last_name").FillAsync(lastName);
        await FillDateAsync("date-of-birth", day, month, year);
        await Page.Locator($"input[name='q_sex'][value='{sex}']").CheckAsync();
        if (upn.Length > 0)
            await Page.Locator("#q_upn").FillAsync(upn);
        await ContinueAsync();
    }

    // QuestionPartialModel renders date inputs as q_<id>_day/_month/_year, where the question
    // id's dashes become underscores.
    private async Task FillDateAsync(string questionId, string day, string month, string year)
    {
        var baseId = $"q_{questionId.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day);
        await Page.Locator($"#{baseId}_month").FillAsync(month);
        await Page.Locator($"#{baseId}_year").FillAsync(year);
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
}
