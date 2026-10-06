using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#304117 — the KS4 Remove "Child missing education" page offers Ground H, Ground I and Other.
// Other is auto-rejected by the rules engine (proven at unit and integration tier against the
// seed rules); in the browser the page must simply offer it and carry the user on to evidence.
[Collection("E2E")]
public sealed class ChildMissingEducationOptionsTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id as
    // Ks4JourneyTests and PermanentExclusionCopyTests).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — first row in SeedPupilData (Pincl 401, not an add-back,
    // so the PupilIsNotAddBack-gated child-missing-education reason option is visible).
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    [RetryFact]
    public async Task RemoveFlow_ChildMissingEducation_OffersGroundHGroundIAndOther()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToReasonPageAsync("child-missing-education");

        var radios = Page.Locator("input[name='q_why_removed']");
        await Expect(radios).ToHaveCountAsync(3);
        Assert.Equal(
            new[] { "not-returned-after-agreed-leave", "no-agreed-leave-or-reason", "other" },
            await radios.EvaluateAllAsync<string[]>("els => els.map(e => e.value)"));

        // _Radio.cshtml renders each option id as {fieldName}-{value}.
        await Expect(Page.Locator("label[for='q_why_removed-not-returned-after-agreed-leave']"))
            .ToHaveTextAsync("Not come back after an agreed period of leave");
        await Expect(Page.Locator("label[for='q_why_removed-no-agreed-leave-or-reason']"))
            .ToHaveTextAsync("Been absent for a long time with no agreed leave and no clear reason");
        await Expect(Page.Locator("label[for='q_why_removed-other']")).ToHaveTextAsync("Other");
        await Expect(Page.Locator("#q_why_removed-other-item-hint")).ToHaveCountAsync(0); // no sub-label on Other
    }

    [RetryFact]
    public async Task RemoveFlow_ChildMissingEducationOther_AdvancesToEvidence()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToReasonPageAsync("child-missing-education");
        await Page.Locator("input[name='q_why_removed'][value='other']").CheckAsync(new() { Force = true });
        await FillDateAsync("date-removed-from-roll", day: 1, month: 9, year: 2025);
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
    }

    [RetryFact]
    public async Task RemoveFlow_ChildMissingEducationGroundH_StillAdvancesToEvidence()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToReasonPageAsync("child-missing-education");
        await Page.Locator("input[name='q_why_removed'][value='not-returned-after-agreed-leave']")
            .CheckAsync(new() { Force = true });
        await FillDateAsync("date-removed-from-roll", day: 1, month: 9, year: 2025);
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

    // QuestionPartialModel renders date inputs as q_<id>_day/_month/_year, where the question
    // id's dashes become underscores.
    private async Task FillDateAsync(string fieldName, int day, int month, int year)
    {
        var baseId = $"q_{fieldName.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day.ToString());
        await Page.Locator($"#{baseId}_month").FillAsync(month.ToString());
        await Page.Locator($"#{baseId}_year").FillAsync(year.ToString());
    }

    // WhatToChange -> Remove -> pupil search -> a reason option -> that reason's page.
    private async Task NavigateToReasonPageAsync(string reasonValue)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");

        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(PupilSurname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");

        await Page.Locator($"input[name='q_reason'][value='{reasonValue}']")
            .CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{reasonValue}");
    }
}
