using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#297310: the "Add a pupil not found on my DfE pupil roll" journey (simple path), driven
// through a real browser.
//
// The Add journey has no pupil-search step — the learner-details page's own answers ARE the
// pupil (AddPupilJourney.BuildPupil). These tests cover what only a browser can: that the whole
// page chain holds together across redirects with no dataset pupil behind it, that the future-date
// rules and required-field errors render correctly, and that a Post16 window (no Add flow exists
// for it) never offers the option.
[Collection("E2E")]
public sealed class AddPupilJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId) — Add doesn't need
    // its pupil blobs, only an open KS4June window.
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // The seeded Post16 window — no Add_Post16.json flow exists, so the option must not appear.
    private static readonly Guid Post16WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    [RetryFact(3)]
    public async Task HappyPath_SubmitsAndShowsAReference()
    {
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Newpupil",
            day: "1", month: "9", year: "2010",
            sex: "F", upn: "A123456789012");

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/admission-details");
        await FillDateAsync("admission-date", "1", "9", "2025");
        await Page.Locator("input[name='q_year_group'][value='10']").CheckAsync(new() { Force = true });
        await Page.Locator("input[name='q_sen_status'][value='N']").CheckAsync(new() { Force = true });
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/evidence");
        await Page.Locator("#q_how_evidence_supports").FillAsync("Pupil joined mid-term from another school.");
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/summary");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Alice Newpupil", summary);
        Assert.Contains("First name", summary);
        Assert.Contains("Year group", summary);
        Assert.Contains("SEN status", summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var match = System.Text.RegularExpressions.Regex.Match(panel, @"CYPMD_KS4June_[0-9A-F]{7}");
        Assert.True(match.Success, $"No reference number in the confirmation panel: {panel}");
    }

    [RetryFact(3)]
    public async Task MissingRequiredFields_ShowTheErrors()
    {
        await StartAddJourneyAsync();

        await ContinueAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var text = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains("Enter the pupil's first name", text);
        Assert.Contains("Enter the pupil's last name", text);
        Assert.Contains("Enter the pupil's date of birth", text);
        Assert.Contains("Select the pupil's sex", text);
        Assert.DoesNotContain("Unique pupil number", text);
    }

    [RetryFact(3)]
    public async Task FutureDateOfBirth_IsBlocked()
    {
        await StartAddJourneyAsync();

        await FillLearnerDetailsAsync(
            firstName: "Alice", lastName: "Newpupil",
            day: "1", month: "9", year: "2099",
            sex: "F", upn: "");

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var text = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains("Date of birth must be in the past", text);
    }

    [RetryFact(3)]
    public async Task Post16Window_DoesNotOfferAdd()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        var option = Page.Locator("input[name='SelectedWhatToChange'][value='Add']");
        Assert.Equal(0, await option.CountAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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
