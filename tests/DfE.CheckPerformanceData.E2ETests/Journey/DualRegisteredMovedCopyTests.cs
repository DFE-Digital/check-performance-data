using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue 852 — the question and error text on the KS4 Remove "dual-registered-moved" page's
// DfE-number question now name accountability rather than exam results:
//   * question reads "What is the DfE number of the school where accountability for {pupil}
//     should be transferred to?";
//   * the empty-field error reads "Enter the DfE number of the school where accountability for
//     {pupil} should be transferred to";
//   * a malformed number keeps the generic DfeNumber format error, and the sibling
//     DfE-number questions keep their own wording (see PermanentExclusionCopyTests).
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class DualRegisteredMovedCopyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id as
    // Ks4JourneyTests and RequestSubmissionPage).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — first row in SeedPupilData (same pupil as
    // PermanentExclusionCopyTests; not an add-back, so the dual-registered-moved reason
    // option is visible).
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    private const string QuestionWording =
        "What is the DfE number of the school where accountability for Alice Smith should be transferred to?";

    private const string AccountabilityErrorMessage =
        "Enter the DfE number of the school where accountability for Alice Smith should be transferred to";

    // ── FR-001: the question reads the new accountability wording ──

    [RetryFact]
    public async Task RemoveFlow_DualRegisteredMovedDfeNumberLabel_ReadsAccountabilityWording()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToDualRegisteredMovedPageAsync();

        await Expect(Page.Locator("label[for='q_dual_registered_moved_dfe_number']"))
            .ToHaveTextAsync(QuestionWording);
    }

    // ── FR-002: the empty field surfaces the new error ──

    [RetryFact]
    public async Task RemoveFlow_DualRegisteredMovedDfeNumberBlank_ShowsAccountabilityError()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToDualRegisteredMovedPageAsync();
        await ContinueAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var summaryText = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains(AccountabilityErrorMessage, summaryText);
        Assert.Contains("/page/dual-registered-moved", Page.Url);
    }

    // ── SC-002: a malformed number keeps the generic DfeNumber format error ──

    [RetryFact]
    public async Task RemoveFlow_DualRegisteredMovedDfeNumberMalformed_KeepsGenericFormatError()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToDualRegisteredMovedPageAsync();
        await Page.Locator("#q_dual_registered_moved_dfe_number").FillAsync("1 2 3");
        await ContinueAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var summaryText = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains("Enter a DfE number in the format 123/4567 or 1234567", summaryText);
        Assert.DoesNotContain("accountability", summaryText);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

    private async Task NavigateToDualRegisteredMovedPageAsync()
        => await NavigateToReasonPageAsync("dual-registered-moved");

    // WhatToChange -> Remove -> pupil search -> a reason option -> that reason's page.
    // Same navigation as PermanentExclusionCopyTests.
    private async Task NavigateToReasonPageAsync(string reasonValue)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync();
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
            .CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{reasonValue}");
    }
}
