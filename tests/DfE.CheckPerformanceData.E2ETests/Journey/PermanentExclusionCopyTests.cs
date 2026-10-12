using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue 496 — the label and error text on the KS4 Remove "permanent-exclusion" page's DfE-number
// question do not match design:
//   * label must read "What is the DfE number of the school which permanently excluded {pupil}?"
//     (design adds "permanently"; there is deliberately no "the" before the pupil name);
//   * all failures (blank or malformed) must surface the single "Enter the 7 digit DfE number of
//     the school which permanently excluded the pupil" message.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class PermanentExclusionCopyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id as
    // Ks4JourneyTests and RequestSubmissionPage).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — first row in SeedPupilData (Pincl 401, not an add-back,
    // so the PupilIsNotAddBack-gated permanent-exclusion reason option is visible).
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    // ── US1: the DfE-number label matches design (issue-496 copy) ──

    [RetryFact]
    public async Task RemoveFlow_PermanentExclusionDfeNumberLabel_MatchesDesign()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToPermanentExclusionPageAsync();

        await Expect(Page.Locator("label[for='q_permanent_exclusion_dfe_number']"))
            .ToHaveTextAsync("What is the DfE number of the school which permanently excluded Alice Smith?");
    }

    // ── US2: every failure surfaces the single issue-496 error message (FR-004) ──

    private const string DesignErrorMessage =
        "Enter the 7 digit DfE number of the school which permanently excluded the pupil";

    [RetryFact]
    public async Task RemoveFlow_PermanentExclusionDfeNumberBlank_ShowsSingleDesignMessage()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToPermanentExclusionPageAsync();
        await ContinueAsync();

        await AssertDesignErrorMessageAsync();
    }

    [RetryFact]
    public async Task RemoveFlow_PermanentExclusionDfeNumberMalformed_ShowsSingleDesignMessage()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToPermanentExclusionPageAsync();
        await Page.Locator("#q_permanent_exclusion_dfe_number").FillAsync("1 2 3");
        await ContinueAsync();

        await AssertDesignErrorMessageAsync();
    }

    [RetryFact]
    public async Task RemoveFlow_PermanentExclusionDfeNumberValid_AdvancesToEvidence()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToPermanentExclusionPageAsync();
        await Page.Locator("#q_permanent_exclusion_dfe_number").FillAsync("1234567");
        await FillDateAsync("date-pupil-excluded", day: 1, month: 9, year: 2025);
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
    }

    // ── US3: FR-008 — the four sibling DfE-number questions are untouched ──

    [RetryFact]
    public async Task RemoveFlow_PermanentlyExcludedSibling_DfeNumberLabelUnchanged()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        // "Permanently excluded from current school" — the reason whose page also carries a
        // DfE-number question but must NOT take the issue-496 wording.
        await NavigateToReasonPageAsync("permanently-excluded");

        await Expect(Page.Locator("label[for='q_permanently_excluded_dfe_number']"))
            .ToHaveTextAsync("What is the DfE number of the school Alice Smith went to? (Optional)");
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

    // Every failure renders as a GOV.UK error summary carrying the single design message, and the
    // URL stays on the permanent-exclusion page.
    private async Task AssertDesignErrorMessageAsync()
    {
        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var summaryText = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains(DesignErrorMessage, summaryText);

        Assert.Contains("/page/permanent-exclusion", Page.Url);
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

    // The excluding-school page is reached via the "Admitted following permanent exclusion" reason,
    // whose value differs from the page id.
    private async Task NavigateToPermanentExclusionPageAsync()
        => await NavigateToReasonPageAsync("permanent-exclusion");
}