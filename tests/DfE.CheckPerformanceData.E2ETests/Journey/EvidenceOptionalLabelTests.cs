using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using DfE.CheckPerformanceData.E2ETests.Retrying;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue #634 / AB#309468: on the shared removal evidence page, the two fields become optional
// when the English-not-first-language auto-rejection rule applies, but their labels did not say
// so. These tests drive the real journey and pin the visible labels, plus the guard that every
// other reason still presents mandatory evidence.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class EvidenceOptionalLabelTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";
    private const string DetailsPageId = "english-not-first-language-details";
    private const string Country = "France";
    private const string CountryInput = "#q_country_originally_from-input";

    private const string EvidenceTitle = "Upload files (Optional)";
    private const string ExplanationTitle =
        "How does the evidence demonstrate why the pupil should be removed? (Optional)";

    [RetryFact]
    public async Task RemoveFlow_OtherFirstLanguage_ShowsOptionalOnBothEvidenceLabels_AndSubmitsEmpty()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEalDetailsPageAsync(firstLanguage: "other");
        await CompleteEalDetailsAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");

        await Expect(Page.GetByText(EvidenceTitle, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText(ExplanationTitle, new() { Exact = true })).ToBeVisibleAsync();

        // Both fields are genuinely optional: continue without a file or an explanation.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/summary");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Summary of amendment request" }))
            .ToBeVisibleAsync();
        Assert.Equal(0, await Page.Locator(".govuk-error-summary").CountAsync());
    }

    [RetryFact]
    public async Task RemoveFlow_EalWithBelievedEnglish_EvidenceLabelsStayMandatory()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEalDetailsPageAsync(firstLanguage: "believed-english");
        await CompleteEalDetailsAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");

        await Expect(Page.GetByText("Upload files", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText(
            "How does the evidence demonstrate why the pupil should be removed?",
            new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, await Page.GetByText(EvidenceTitle, new() { Exact = true }).CountAsync());
        Assert.Equal(0, await Page.GetByText(ExplanationTitle, new() { Exact = true }).CountAsync());

        // Mandatory evidence still blocks an empty submit.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");
    }

    // WhatToChange -> Remove -> pupil search -> reason -> first-language -> EAL details.
    private async Task NavigateToEalDetailsPageAsync(string firstLanguage)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");

        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(PupilSurname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");

        await Page.Locator("input[name='q_reason'][value='english-not-first-language']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/english-not-first-language");

        await Page.Locator($"input[name='q_first_language'][value='{firstLanguage}']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{DetailsPageId}");
    }

    private async Task CompleteEalDetailsAsync()
    {
        var input = Page.Locator(CountryInput);
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync("Fran");
        var suggestion = Page.Locator("li[role='option']").GetByText(Country);
        await Expect(suggestion).ToBeVisibleAsync();
        await suggestion.ClickAsync();

        await Page.FillAsync("input[name='q_date_pupil_started_day']", "1");
        await Page.FillAsync("input[name='q_date_pupil_started_month']", "9");
        await Page.FillAsync("input[name='q_date_pupil_started_year']", "2023");
        await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_day']", "1");
        await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_month']", "9");
        await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_year']", "2022");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
    }
}


